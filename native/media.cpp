#include "internal.hpp"
#include <algorithm>
#include <cmath>
#include <regex>
#include <set>
#include <sstream>
#include <winhttp.h>

namespace ovm
{
json default_settings()
{
    return {{"outputDirectory", path_text(default_output())},
            {"outputTemplate", "%(title)s [%(id)s].%(ext)s"},
            {"defaultMode", "Video"},
            {"container", "Mkv"},
            {"videoCodec", "Best"},
            {"maxHeight", 0},
            {"audioFormat", "Best"},
            {"concurrency", 2},
            {"startQueueAutomatically", false},
            {"embedMetadata", true},
            {"embedThumbnail", true},
            {"embedSubtitles", false},
            {"subtitleLanguages", "en.*"},
            {"singleVideoFromPlaylistLinks", true},
            {"useDownloadArchive", false},
            {"cookiesFromBrowser", ""},
            {"rateLimit", ""},
            {"extraArguments", ""},
            {"watchClipboard", true},
            {"linkFilter", "KnownSites"},
            {"extraSites", json::array()},
            {"toolsDirectory", ""},
            {"ytDlpChannel", "Stable"},
            {"checkForToolUpdatesOnStartup", true},
            {"installToolUpdatesAutomatically", true}};
}
json normalize_settings(json input)
{
    auto result = default_settings();
    if (input.is_object())
        for (auto &[key, value] : result.items())
            if (input.contains(key) &&
                (input[key].type() == value.type() || (input[key].is_number_integer() && value.is_number_integer())))
                value = input[key];
    auto choice = [&](const char *key, std::initializer_list<const char *> choices) {
        const auto value = lower(result[key].get<std::string>());
        for (auto c : choices)
            if (lower(c) == value)
            {
                result[key] = c;
                return;
            }
        result[key] = *choices.begin();
    };
    choice("defaultMode", {"Video", "Audio"});
    choice("container", {"Mkv", "Mp4", "Auto"});
    choice("videoCodec", {"Best", "H264", "Hevc", "Av1", "Vp9"});
    choice("audioFormat", {"Best", "Opus", "M4a", "Mp3", "Flac"});
    choice("ytDlpChannel", {"Stable", "Nightly"});
    choice("linkFilter", {"KnownSites", "AnyLink"});
    result["concurrency"] = std::clamp(result["concurrency"].get<int64_t>(), int64_t(1), int64_t(8));
    result["maxHeight"] = std::clamp(result["maxHeight"].get<int64_t>(), int64_t(0), int64_t(16384));
    for (const auto key : {"outputDirectory", "outputTemplate"})
        if (trim(result[key]).empty())
            result[key] = default_settings()[key];
    result["outputDirectory"] = path_text(fs::absolute(path(result["outputDirectory"])));
    auto tools = trim(result["toolsDirectory"]);
    result["toolsDirectory"] = tools.empty() ? "" : path_text(fs::absolute(path(tools)));
    std::set<std::string> sites;
    for (const auto &site : result["extraSites"])
        if (site.is_string())
        {
            auto s = lower(trim(site));
            auto scheme = s.find("://");
            if (scheme != std::string::npos)
                s.erase(0, scheme + 3);
            s = s.substr(0, s.find_first_of("/:?#"));
            if (s.starts_with("www."))
                s.erase(0, 4);
            if (!s.empty())
                sites.insert(s);
        }
    result["extraSites"] = sites;
    return result;
}
std::vector<std::string> known_sites()
{
    return {"youtube.com",    "youtu.be",        "youtube-nocookie.com",
            "vimeo.com",      "dailymotion.com", "dai.ly",
            "twitch.tv",      "kick.com",        "x.com",
            "twitter.com",    "bsky.app",        "threads.net",
            "threads.com",    "tiktok.com",      "instagram.com",
            "facebook.com",   "fb.watch",        "reddit.com",
            "redd.it",        "soundcloud.com",  "bandcamp.com",
            "mixcloud.com",   "bilibili.com",    "b23.tv",
            "nicovideo.jp",   "nico.ms",         "rumble.com",
            "odysee.com",     "bitchute.com",    "streamable.com",
            "archive.org",    "vk.com",          "vkvideo.ru",
            "ok.ru",          "ted.com",         "nebula.tv",
            "floatplane.com", "dropout.tv",      "imgur.com",
            "9gag.com",       "pinterest.com",   "bbc.co.uk",
            "arte.tv",        "ardmediathek.de", "zdf.de",
            "cbc.ca",         "nhk.or.jp"};
}
struct Url
{
    std::string host, route, query, port;
};
static Url parse_url(const std::string &s)
{
    auto w = wide(s);
    URL_COMPONENTS parts{};
    parts.dwStructSize = sizeof(parts);
    parts.dwHostNameLength = parts.dwUrlPathLength = parts.dwExtraInfoLength = static_cast<DWORD>(-1);
    if (!WinHttpCrackUrl(w.c_str(), 0, 0, &parts) ||
        (parts.nScheme != INTERNET_SCHEME_HTTP && parts.nScheme != INTERNET_SCHEME_HTTPS) || !parts.dwHostNameLength)
        throw std::runtime_error("Only valid HTTP or HTTPS URLs are supported.");
    auto extra = utf8(std::wstring(parts.lpszExtraInfo, parts.dwExtraInfoLength));
    extra = extra.substr(0, extra.find('#'));
    const auto default_port = parts.nScheme == INTERNET_SCHEME_HTTP ? 80 : 443;
    return {lower(utf8(std::wstring(parts.lpszHostName, parts.dwHostNameLength))),
            utf8(std::wstring(parts.lpszUrlPath, parts.dwUrlPathLength)), extra,
            parts.nPort == default_port ? "" : ":" + std::to_string(parts.nPort)};
}
static bool matches(std::string host, std::string domain)
{
    while (host.ends_with('.'))
        host.pop_back();
    while (domain.ends_with('.'))
        domain.pop_back();
    return host == domain || (!domain.empty() && host.ends_with("." + domain));
}
static std::string percent_decode(std::string s)
{
    std::string out;
    for (size_t i = 0; i < s.size(); ++i)
    {
        if (s[i] == '%' && i + 2 < s.size() && std::isxdigit(static_cast<unsigned char>(s[i + 1])) &&
            std::isxdigit(static_cast<unsigned char>(s[i + 2])))
        {
            out += static_cast<char>(std::stoi(s.substr(i + 1, 2), nullptr, 16));
            i += 2;
        }
        else
            out += s[i];
    }
    return out;
}
static std::string youtube_id(const Url &u)
{
    std::string id;
    if (matches(u.host, "youtu.be"))
        id = u.route.size() > 1 ? u.route.substr(1, u.route.find('/', 1) - 1) : "";
    else if (matches(u.host, "youtube.com") || matches(u.host, "youtube-nocookie.com"))
    {
        if (u.route == "/watch")
        {
            std::smatch m;
            if (std::regex_search(u.query, m, std::regex("[?&]v=([^&]*)")))
                id = percent_decode(m[1]);
        }
        else
        {
            std::smatch m;
            if (std::regex_search(u.route, m, std::regex("^/(?:shorts|live|embed|v)/([^/]+)")))
                id = m[1];
        }
    }
    return std::regex_match(id, std::regex("[A-Za-z0-9_-]{11}")) ? id : "";
}
std::string link_key(const std::string &url)
{
    auto u = parse_url(url);
    auto id = youtube_id(u);
    if (!id.empty())
        return "youtube:" + id;
    if (u.host.starts_with("www."))
        u.host.erase(0, 4);
    while (u.route.ends_with('/'))
        u.route.pop_back();
    return u.host + u.port + u.route + u.query;
}
static std::string html_decode(std::string text)
{
    for (const auto &[from, to] : std::vector<std::pair<std::string, std::string>>{
             {"&amp;", "&"}, {"&quot;", "\""}, {"&#39;", "'"}, {"&lt;", "<"}, {"&gt;", ">"}})
    {
        size_t pos = 0;
        while ((pos = text.find(from, pos)) != std::string::npos)
        {
            text.replace(pos, from.size(), to);
            pos += to.size();
        }
    }
    return text;
}
std::vector<std::string> extract_links(const std::string &text, const std::string &html)
{
    if (text.size() + html.size() > 16 * 1024 * 1024)
        throw std::runtime_error("Input text exceeds 16 MiB.");
    std::string source = text;
    static const std::regex href(R"(href\s*=\s*["']([^"']+)["'])", std::regex::icase);
    for (auto i = std::sregex_iterator(html.begin(), html.end(), href); i != std::sregex_iterator(); ++i)
        source += '\n' + html_decode((*i)[1]);
    static const std::regex pattern(R"((?:https?://|www\.)[^\s"'<>`\[\]{}|\\^]+)", std::regex::icase);
    std::vector<std::string> out;
    std::set<std::string> keys;
    for (auto i = std::sregex_iterator(source.begin(), source.end(), pattern); i != std::sregex_iterator(); ++i)
    {
        auto url = i->str();
        while (!url.empty())
        {
            char c = url.back();
            if (std::string(".,;:!?*'\"").find(c) != std::string::npos ||
                (c == ')' && std::count(url.begin(), url.end(), ')') > std::count(url.begin(), url.end(), '(')))
                url.pop_back();
            else
                break;
        }
        if (lower(url).starts_with("www."))
            url = "https://" + url;
        try
        {
            auto key = link_key(url);
            if (keys.insert(key).second)
                out.push_back(url);
        }
        catch (const std::exception &)
        {
        }
        if (out.size() >= 10000)
            throw std::runtime_error("At most 10,000 links can be imported at once.");
    }
    return out;
}
bool qualifies(const std::string &url, const json &settings)
{
    auto u = parse_url(url);
    if (settings["linkFilter"] == "AnyLink")
        return true;
    auto sites = known_sites();
    for (const auto &s : settings["extraSites"])
        sites.push_back(s);
    if (std::none_of(sites.begin(), sites.end(), [&](const auto &s) { return matches(u.host, s); }))
        return false;
    if (matches(u.host, "youtube.com") || matches(u.host, "youtu.be") || matches(u.host, "youtube-nocookie.com"))
        return !youtube_id(u).empty() ||
               std::regex_search(u.route, std::regex("^/(?:@[^/]+|playlist|channel|c|user)(?:/|$)"));
    return true;
}
static std::vector<std::string> split_arguments(const std::string &source)
{
    std::vector<std::string> out;
    std::string part;
    char quote = 0;
    bool started = false;
    for (char c : source)
    {
        if (quote)
        {
            if (c == quote)
                quote = 0;
            else
                part += c;
            started = true;
        }
        else if (c == '\'' || c == '"')
        {
            quote = c;
            started = true;
        }
        else if (std::isspace(static_cast<unsigned char>(c)))
        {
            if (started)
            {
                out.push_back(part);
                part.clear();
                started = false;
            }
        }
        else
        {
            part += c;
            started = true;
        }
    }
    if (quote)
        throw std::runtime_error("Unclosed quote in extra yt-dlp arguments.");
    if (started)
        out.push_back(part);
    return out;
}
std::vector<std::string> download_arguments(const json &s, const fs::path &tools, const std::string &mode,
                                            const std::string &url)
{
    (void)parse_url(url);
    std::vector<std::string> a = {"--ignore-config",
                                  "--windows-filenames",
                                  "--encoding",
                                  "utf-8",
                                  "--newline",
                                  "--no-quiet",
                                  "--no-simulate",
                                  "--color",
                                  "no_color",
                                  "--progress-template",
                                  "download:@@P "
                                  "%(progress.downloaded_bytes)s|%(progress.total_bytes)s|%(progress.total_bytes_"
                                  "estimate)s|%(progress.speed)s|%(progress.eta)s",
                                  "--print",
                                  "before_dl:@@T %(title)s",
                                  "--print",
                                  "after_move:@@F %(filepath)s"};
    auto add = [&](std::initializer_list<std::string> values) { a.insert(a.end(), values); };
    if (mode == "Video")
    {
        const std::map<std::string, std::string> codecs = {
            {"H264", "avc1"}, {"Hevc", "hev1|hvc1|hevc"}, {"Av1", "av01"}, {"Vp9", "vp0?9"}};
        const auto found = codecs.find(s["videoCodec"]);
        std::string filter = found == codecs.end() ? "" : "[vcodec~='^(" + found->second + ")']";
        add({"-f", "bv*" + filter + "+ba/b" + filter});
        if (s["maxHeight"].get<int>() > 0)
            add({"-S", "res:" + std::to_string(s["maxHeight"].get<int>())});
        if (s["container"] != "Auto")
        {
            auto container = lower(s["container"]);
            add({"--merge-output-format", container, "--remux-video", container});
        }
        if (s["embedSubtitles"] == true)
        {
            add({"--embed-subs"});
            if (!trim(s["subtitleLanguages"]).empty())
                add({"--sub-langs", s["subtitleLanguages"]});
        }
    }
    else if (mode == "Audio")
        add({"-f", "ba/b", "-x", "--audio-format", lower(s["audioFormat"]), "--audio-quality", "0"});
    else
        throw std::runtime_error("Unknown download mode.");
    if (s["embedMetadata"] == true)
        add({"--embed-metadata"});
    if (s["embedThumbnail"] == true)
        add({"--embed-thumbnail"});
    if (s["singleVideoFromPlaylistLinks"] == true)
        add({"--no-playlist"});
    if (!trim(s["cookiesFromBrowser"]).empty())
        add({"--cookies-from-browser", s["cookiesFromBrowser"]});
    if (!trim(s["rateLimit"]).empty())
        add({"-r", s["rateLimit"]});
    // Keep all media writes on the disk being monitored, including intermediate files.
    auto output_template = s["outputTemplate"].get<std::string>();
    if (path(output_template).is_absolute() || output_template.find("..") != std::string::npos ||
        output_template.find(':') != std::string::npos || output_template.find('/') != std::string::npos ||
        output_template.find('\\') != std::string::npos)
        throw std::runtime_error("Output template must be a file name inside the chosen output folder.");
    add({"-o", output_template, "-P", s["outputDirectory"], "--ffmpeg-location", path_text(tools), "--js-runtimes",
         "deno:" + path_text(tools / L"deno.exe")});
    if (s["useDownloadArchive"] == true)
        add({"--download-archive", path_text(path(s["outputDirectory"]) / L"archive.txt")});
    for (const auto &arg : split_arguments(s["extraArguments"]))
    {
        if (arg.starts_with("-P") || arg.starts_with("--paths") || arg.starts_with("-o") ||
            arg.starts_with("--output") || arg.starts_with("--config") || arg.starts_with("--exec") || arg == "--")
            throw std::runtime_error(
                "Extra arguments cannot override output paths, configuration, or execute commands.");
        a.push_back(arg);
    }
    add({"--", url});
    return a;
}
json new_item(const std::string &id, const std::string &url, const std::string &mode)
{
    return {{"id", id},
            {"url", url},
            {"mode", mode},
            {"status", "Queued"},
            {"title", ""},
            {"error", ""},
            {"filePath", ""},
            {"progress", 0.0},
            {"indeterminate", false},
            {"stage", ""},
            {"downloaded", nullptr},
            {"total", nullptr},
            {"speed", nullptr},
            {"eta", nullptr},
            {"streams", 0},
            {"stream", 0},
            {"playlistIndex", 0},
            {"playlistCount", 0},
            {"logs", json::array()}};
}
void apply_output(json &item, const std::string &line, bool error)
{
    auto log = [&](const std::string &text) {
        auto &logs = item["logs"];
        if (logs.size() >= 300)
            logs.erase(logs.begin());
        logs.push_back(text);
    };
    if (line.starts_with("@@T "))
        item["title"] = line.substr(4);
    else if (line.starts_with("@@F "))
    {
        item["filePath"] = line.substr(4);
        log("Saved: " + line.substr(4));
    }
    else if (line.starts_with("@@P "))
    {
        std::istringstream fields(line.substr(4));
        std::vector<json> values;
        std::string v;
        while (std::getline(fields, v, '|'))
        {
            try
            {
                size_t n;
                auto number = std::stod(v, &n);
                values.push_back(n == v.size() && std::isfinite(number) ? json(number) : json(nullptr));
            }
            catch (...)
            {
                values.push_back(nullptr);
            }
        }
        while (values.size() < 5)
            values.push_back(nullptr);
        item["downloaded"] = values[0];
        if (!values[1].is_null())
            item["total"] = values[1];
        else if (!values[2].is_null())
            item["total"] = values[2];
        item["speed"] = values[3];
        item["eta"] = values[4];
        item["stage"] = "";
        if (item["downloaded"].is_number() && item["total"].is_number() && item["total"].get<double>() > 0)
        {
            double fraction = std::clamp(item["downloaded"].get<double>() / item["total"].get<double>(), 0.0, 1.0);
            int streams = item["streams"], stream = item["stream"], count = item["playlistCount"],
                index = item["playlistIndex"];
            if (streams > 1 && stream > 0)
                fraction = (std::min(stream, streams) - 1 + fraction) / streams;
            if (count > 1 && index > 0)
                fraction = (std::min(index, count) - 1 + fraction) / count;
            item["progress"] = fraction * 100;
            item["indeterminate"] = false;
        }
    }
    else
    {
        if (error && line.starts_with("ERROR"))
            item["error"] = line;
        if (line.starts_with("[download] Destination:"))
        {
            item["stream"] = item["stream"].get<int>() + 1;
            item["stage"] = "";
            item["downloaded"] = nullptr;
            item["total"] = nullptr;
            item["indeterminate"] = true;
        }
        std::smatch m;
        if (std::regex_search(line, m, std::regex("Downloading item ([0-9]+) of ([0-9]+)")))
        {
            item["playlistIndex"] = std::stoi(m[1]);
            item["playlistCount"] = std::stoi(m[2]);
            item["streams"] = 0;
            item["stream"] = 0;
        }
        auto at = line.find("format(s): ");
        if (at != std::string::npos)
        {
            auto formats = line.substr(at + 11);
            item["streams"] = 1 + std::count(formats.begin(), formats.end(), '+');
            item["stream"] = 0;
        }
        if (line.starts_with('[') && line.find(']') != std::string::npos)
        {
            auto stage = line.substr(1, line.find(']') - 1);
            if (stage == "Merger" || stage == "ExtractAudio" || stage == "Metadata" || stage == "VideoRemuxer" ||
                stage.starts_with("Embed") || stage.starts_with("Fixup") || stage == "MoveFiles" ||
                stage == "ThumbnailsConvertor")
            {
                item["stage"] = stage;
                item["indeterminate"] = true;
            }
        }
        log(line);
    }
}
} // namespace ovm
