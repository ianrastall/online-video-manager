#include "internal.hpp"
#include <algorithm>
#include <regex>
#include <sstream>

namespace ovm
{
fs::path tools_path(const json &settings, const fs::path &home)
{
    auto configured = settings.value("toolsDirectory", std::string());
    auto directory = configured.empty() ? home / L"tools" : path(configured);
    // Pass real paths to yt-dlp's --ffmpeg-location and --js-runtimes too; its
    // subprocesses do not necessarily inherit the app's MSIX redirection.
    return fs::exists(directory) ? fs::canonical(directory) : directory;
}
std::string executable_name(const std::string &tool)
{
    if (tool == "yt-dlp")
        return "yt-dlp.exe";
    if (tool == "deno")
        return "deno.exe";
    if (tool == "ffmpeg")
        return "ffmpeg.exe";
    throw std::runtime_error("Unknown tool.");
}
bool tools_ready(const fs::path &directory)
{
    for (auto name : {L"yt-dlp.exe", L"deno.exe", L"ffmpeg.exe", L"ffprobe.exe"})
        if (!fs::is_regular_file(directory / name))
            return false;
    return true;
}
std::string checksum_for(const std::string &sums, const std::string &name)
{
    std::istringstream lines(sums);
    std::string line;
    const std::regex gnu(R"(^([a-fA-F0-9]{64})\s+\*?(.+)$)");
    const std::regex bsd(R"(^SHA256\s*\((.+)\)\s*=\s*([a-fA-F0-9]{64})$)");
    while (std::getline(lines, line))
    {
        line = trim(line);
        std::smatch m;
        if (std::regex_match(line, m, gnu) && trim(m[2]) == name)
            return lower(m[1]);
        if (std::regex_match(line, m, bsd) && trim(m[1]) == name)
            return lower(m[2]);
        if (std::regex_match(line, std::regex("[a-fA-F0-9]{64}")))
            return lower(line);
    }
    // Deno's Windows releases publish PowerShell Get-FileHash formatted output.
    std::smatch algorithm, hash, file;
    if (std::regex_search(sums, algorithm, std::regex(R"(Algorithm\s*:\s*(\w+))", std::regex::icase)) &&
        lower(algorithm[1]) == "sha256" &&
        std::regex_search(sums, hash, std::regex(R"(Hash\s*:\s*([a-fA-F0-9]{64})\b)", std::regex::icase)) &&
        std::regex_search(sums, file, std::regex(R"(Path\s*:\s*([^\r\n]+))", std::regex::icase)) &&
        path(trim(file[1])).filename() == path(name))
        return lower(hash[1]);
    throw std::runtime_error("No SHA-256 checksum found for " + name);
}
json latest_release(const std::string &tool, const std::string &channel, std::atomic_bool &stop)
{
    std::string endpoint, asset_name, sums;
    if (tool == "yt-dlp")
    {
        endpoint = "repos/yt-dlp/" + std::string(channel == "Nightly" ? "yt-dlp-nightly-builds" : "yt-dlp") +
                   "/releases/latest";
        asset_name = "yt-dlp.exe";
        sums = "SHA2-256SUMS";
    }
    else if (tool == "deno")
    {
        endpoint = "repos/denoland/deno/releases/latest";
        asset_name = "deno-x86_64-pc-windows-msvc.zip";
        sums = asset_name + ".sha256sum";
    }
    else if (tool == "ffmpeg")
    {
        endpoint = "repos/BtbN/FFmpeg-Builds/releases?per_page=10";
        sums = "checksums.sha256";
    }
    else
        throw std::runtime_error("Unknown tool.");
    auto releases = json::parse(http_get("https://api.github.com/" + endpoint, stop));
    if (!releases.is_array())
        releases = json::array({releases});
    for (const auto &release : releases)
    {
        if (tool == "ffmpeg" && !release.value("tag_name", std::string()).starts_with("autobuild-"))
            continue;
        json asset = nullptr;
        std::string checksum_url;
        for (const auto &candidate : release.at("assets"))
        {
            auto name = candidate.at("name").get<std::string>();
            if (name == sums)
                checksum_url = candidate.at("browser_download_url");
            if ((tool == "ffmpeg" && name.starts_with("ffmpeg-N-") && name.ends_with("-win64-gpl-shared.zip")) ||
                name == asset_name)
                asset = candidate;
        }
        if (asset.is_null())
            continue;
        if (checksum_url.empty())
            throw std::runtime_error("Release has no checksum; refusing installation.");
        std::string version = release.at("tag_name");
        if (tool == "deno" && version.starts_with('v'))
            version.erase(0, 1);
        if (tool == "ffmpeg")
        {
            auto name = asset["name"].get<std::string>();
            version = name.substr(7, name.size() - 7 - std::string("-win64-gpl-shared.zip").size());
        }
        return {{"version", version},
                {"assetName", asset["name"]},
                {"assetUrl", asset["browser_download_url"]},
                {"checksumUrl", checksum_url},
                {"size", asset.value("size", 0ull)}};
    }
    throw std::runtime_error("No compatible Windows x64 release found for " + tool);
}
std::string local_version(const fs::path &directory, const std::string &tool, std::atomic_bool &stop,
                          std::string *failure)
{
    if (failure)
        failure->clear();
    const auto executable = directory / path(executable_name(tool));
    if (!fs::is_regular_file(executable) || (tool == "ffmpeg" && !fs::is_regular_file(directory / L"ffprobe.exe")))
        return "";
    try
    {
        auto output = capture(executable,
                              tool == "ffmpeg" ? std::vector<std::string>{"-hide_banner", "-version"}
                                               : std::vector<std::string>{"--version"},
                              stop);
        auto first = output.substr(0, output.find('\n'));
        if (tool == "yt-dlp")
            return first.empty() ? "unknown" : first;
        std::istringstream tokens(first);
        std::string a, b, c;
        tokens >> a >> b >> c;
        if (tool == "deno")
            return a == "deno" ? b : "unknown";
        if (a == "ffmpeg" && b == "version")
        {
            if (c.starts_with("N-"))
            {
                size_t i = c.find('-', 2);
                if (i != std::string::npos)
                {
                    i = c.find('-', i + 1);
                    if (i != std::string::npos)
                        c.resize(i);
                }
            }
            return c;
        }
    }
    catch (const Canceled &)
    {
        throw;
    }
    catch (const std::exception &ex)
    {
        if (failure)
            *failure = ex.what();
    }
    return "unknown";
}
void install_tool(const fs::path &directory, const std::string &tool, const json &release, std::atomic_bool &stop,
                  const InstallProgress &progress)
{
    const auto name = release.at("assetName").get<std::string>();
    if (path(name).filename() != path(name) || name.find(':') != std::string::npos)
        throw std::runtime_error("Unsafe release filename.");
    const auto checksum_url = release.value("checksumUrl", std::string());
    if (checksum_url.empty())
        throw std::runtime_error("Missing upstream checksum.");
    fs::create_directories(directory);
    ensure_disk(directory, std::max(disk_reserve, release.value("size", 0ull) * 4));
    const auto staging = directory / (L".ovm-staging-" + std::to_wstring(GetCurrentProcessId()) + L"-" +
                                      std::to_wstring(GetTickCount64()));
    fs::create_directories(staging);
    struct Cleanup
    {
        fs::path p;
        ~Cleanup()
        {
            if (!p.empty())
            {
                std::error_code ec;
                fs::remove_all(p, ec);
            }
        }
    } cleanup{staging};
    const auto archive = staging / path(name);
    progress("Downloading " + name, 0);
    http_download(release.at("assetUrl"), archive, stop, [&](uint64_t done, uint64_t total) {
        ensure_disk(directory);
        progress("Downloading " + name, total ? static_cast<double>(done) / static_cast<double>(total) : -1);
    });
    progress("Verifying checksum", -1);
    const auto expected = checksum_for(http_get(checksum_url, stop), name);
    if (sha256(archive) != expected)
        throw std::runtime_error("Checksum mismatch for " + name + "; original installation preserved.");
    canceled(stop);
    progress("Unpacking", -1);
    const auto unpacked = staging / L"out";
    fs::create_directories(unpacked);
    std::vector<fs::path> files;
    if (tool == "yt-dlp")
    {
        fs::copy_file(archive, unpacked / L"yt-dlp.exe");
        files.push_back(unpacked / L"yt-dlp.exe");
    }
    else
        files = extract_zip(archive, unpacked, tool, stop);
    if (!fs::is_regular_file(unpacked / path(executable_name(tool))) ||
        (tool == "ffmpeg" && !fs::is_regular_file(unpacked / L"ffprobe.exe")))
        throw std::runtime_error("Incomplete tool archive (required executable or ffprobe missing).");
    canceled(stop);
    progress("Installing", -1);
    try
    {
        commit_tool_files(directory, staging, tool, release, files, stop);
    }
    catch (const RollbackFailed &)
    {
        cleanup.p.clear();
        throw;
    }
    progress("Installed", 1);
}
void commit_tool_files(const fs::path &directory, const fs::path &staging, const std::string &tool, const json &release,
                       const std::vector<fs::path> &files, std::atomic_bool &stop)
{
    // The engine excludes downloads during this transaction. All files roll back together.
    const auto backup = staging / L"backup";
    fs::create_directories(backup);
    std::vector<fs::path> moved, installed;
    try
    {
        for (const auto &file : files)
        {
            canceled(stop);
            const auto destination = directory / file.filename();
            if (fs::exists(destination))
            {
                fs::rename(destination, backup / file.filename());
                moved.push_back(file.filename());
            }
            fs::rename(file, destination);
            installed.push_back(file.filename());
        }
        // Check the executables before committing the new installation.
        std::string failure;
        const auto version = local_version(directory, tool, stop, &failure);
        if (version.empty() || version == "unknown")
            throw std::runtime_error("Installed tool failed its version check." +
                                     (failure.empty() ? "" : " " + failure));
        auto manifest = read_json(directory / L".ovm-tools.json", json::object());
        if (!manifest.is_object())
            manifest = json::object();
        manifest[tool] = release.at("version");
        write_json(directory / L".ovm-tools.json", manifest);
    }
    catch (...)
    {
        auto failure = std::current_exception();
        std::string rollback_error;
        for (const auto &file : installed)
        {
            std::error_code ec;
            fs::remove(directory / file, ec);
            if (ec)
                rollback_error = ec.message();
        }
        for (const auto &file : moved)
        {
            std::error_code ec;
            fs::rename(backup / file, directory / file, ec);
            if (ec)
                rollback_error = ec.message();
        }
        if (!rollback_error.empty())
        {
            throw RollbackFailed("Tool rollback needs attention; backup retained at " + path_text(backup) + ": " +
                                 rollback_error);
        }
        std::rethrow_exception(failure);
    }
}
} // namespace ovm
