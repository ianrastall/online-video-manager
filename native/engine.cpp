#include "internal.hpp"
#include <algorithm>
#include <set>

namespace ovm
{
Engine::Engine(fs::path home) : home_(fs::absolute(home.empty() ? default_home() : home).lexically_normal())
{
    fs::create_directories(home_);
    ownership_.value = CreateFileW((home_ / L"engine.lock").c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr,
                                   OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (!ownership_)
        throw std::runtime_error("Another OVM engine already owns this data directory.");
    settings_ = normalize_settings(read_json(home_ / L"settings.json", json::object()));
    auto saved = read_json(home_ / L"queue.json", json::array());
    if (saved.is_array())
        for (const auto &row : saved)
        {
            try
            {
                const auto url = row.at("url").get<std::string>();
                (void)link_key(url);
                auto item = new_item(std::to_string(next_id_++), url, row.value("mode", "Video"));
                auto status = row.value("status", row.value("state", "Queued"));
                item["status"] = status == "Running" || status == "Completed" ? "Queued" : status;
                if (row.contains("error") && row["error"].is_string())
                    item["error"] = row["error"];
                items_.push_back(item);
            }
            catch (const std::exception &)
            {
                notify("Some invalid saved queue entries were ignored.");
            }
        }
    saved = read_json(home_ / L"inbox.json", json::array());
    std::set<std::string> seen;
    if (saved.is_array())
        for (const auto &row : saved)
            try
            {
                auto url = row.is_string() ? row.get<std::string>() : row.at("url").get<std::string>();
                if (seen.insert(link_key(url)).second)
                    inbox_.push_back(url);
            }
            catch (const std::exception &)
            {
                notify("Some invalid saved clipboard entries were ignored.");
            }
    for (const auto &tool : {"yt-dlp", "deno", "ffmpeg"})
        tools_.push_back({{"id", tool},
                          {"installed", ""},
                          {"latest", ""},
                          {"error", ""},
                          {"activity", ""},
                          {"progress", 0.0},
                          {"working", false}});
    if (!items_.empty())
        notify("Restored unfinished downloads. Press Start to resume.");
    scheduler_ = std::thread([this] { schedule(); });
}
Engine::~Engine()
{
    {
        std::lock_guard lock(mutex_);
        stopping_ = true;
        running_ = false;
        for (auto &job : jobs_)
            *job->stop = true;
        if (updater_)
            *updater_->stop = true;
        try
        {
            save_queue();
            save_inbox();
        }
        catch (...)
        {
        }
    }
    wake_.notify_all();
    if (scheduler_.joinable())
        scheduler_.join();
    for (auto &job : jobs_)
        if (job->thread.joinable())
            job->thread.join();
    if (updater_ && updater_->thread.joinable())
        updater_->thread.join();
}
void Engine::save_queue()
{
    json saved = json::array();
    for (const auto &item : items_)
        if (item["status"] != "Completed")
            saved.push_back({{"url", item["url"]},
                             {"mode", item["mode"]},
                             {"status", item["status"] == "Running" ? json("Queued") : item["status"]},
                             {"error", item["error"]}});
    try
    {
        write_json(home_ / L"queue.json", saved);
    }
    catch (...)
    {
        running_ = false;
        throw;
    }
}
void Engine::save_inbox()
{
    write_json(home_ / L"inbox.json", inbox_);
}
void Engine::notify(const std::string &message)
{
    notice_ = message;
    ++notice_id_;
    ++revision_;
}
void Engine::log(const std::string &message)
{
    if (logs_.size() >= 200)
        logs_.erase(logs_.begin());
    logs_.push_back(message);
    ++revision_;
}
size_t Engine::active() const
{
    return std::count_if(jobs_.begin(), jobs_.end(), [](const auto &job) { return !job->done; });
}
json *Engine::find_item(const std::string &id)
{
    for (auto &item : items_)
        if (item["id"] == id)
            return &item;
    return nullptr;
}
json Engine::snapshot() const
{
    return {{"revision", revision_},
            {"settings", settings_},
            {"dataDirectory", path_text(home_)},
            {"toolsDirectory", path_text(tools_path(settings_, home_))},
            {"knownSites", known_sites()},
            {"items", items_},
            {"inbox", inbox_},
            {"running", running_},
            {"toolsBusy", tools_busy_},
            {"tools", tools_},
            {"toolStatus", status_},
            {"toolLogs", logs_},
            {"notice", notice_},
            {"noticeId", notice_id_}};
}
json Engine::enqueue(const std::vector<std::string> &links, const std::string &mode)
{
    if (mode != "Video" && mode != "Audio")
        throw std::runtime_error("Invalid download mode.");
    std::set<std::string> pending;
    for (const auto &i : items_)
        if (i["status"] == "Queued" || i["status"] == "Running")
            pending.insert(link_key(i["url"]));
    int added = 0, skipped = 0;
    for (const auto &url : links)
    {
        if (!pending.insert(link_key(url)).second)
        {
            ++skipped;
            continue;
        }
        items_.push_back(new_item(std::to_string(next_id_++), url, mode));
        ++added;
    }
    if (added)
    {
        save_queue();
        if (settings_["startQueueAutomatically"] == true)
            running_ = true;
    }
    notify(std::to_string(added) + " link(s) added; " + std::to_string(skipped) + " duplicate(s) skipped.");
    return {{"added", added}, {"skipped", skipped}};
}
json Engine::execute(const json &r)
{
    std::lock_guard lock(mutex_);
    if (stopping_)
        throw std::runtime_error("Engine is shutting down.");
    const auto op = r.at("operation").get<std::string>();
    json result = json::object();
    if (op == "snapshot")
        return {{"state", snapshot()}};
    if (op == "initialize")
    {
        if (!initialized_)
        {
            initialized_ = true;
            start_tools(settings_["checkForToolUpdatesOnStartup"], settings_["installToolUpdatesAutomatically"]);
        }
    }
    else if (op == "settings.set")
    {
        auto candidate = normalize_settings(r.at("settings"));
        if ((tools_busy_ || active()) && tools_path(candidate, home_) != tools_path(settings_, home_))
            throw std::runtime_error("Wait for active work before changing the tools folder.");
        write_json(home_ / L"settings.json", candidate);
        settings_ = candidate;
    }
    else if (op == "settings.resetTemplate")
    {
        auto candidate = settings_;
        candidate["outputTemplate"] = default_settings()["outputTemplate"];
        write_json(home_ / L"settings.json", candidate);
        settings_ = candidate;
    }
    else if (op == "links.preview")
    {
        auto links = extract_links(r.value("text", ""), r.value("html", ""));
        result["links"] = links;
        result["count"] = links.size();
    }
    else if (op == "queue.add" || op == "queue.import")
    {
        auto text = op == "queue.import" ? read_text(path(r.at("path"))) : r.value("text", "");
        auto links = extract_links(text, r.value("html", ""));
        if (links.empty())
            throw std::runtime_error("No HTTP or HTTPS links found.");
        result = enqueue(links, r.value("mode", settings_["defaultMode"].get<std::string>()));
    }
    else if (op == "clipboard.collect")
    {
        auto links = extract_links(r.value("text", ""), r.value("html", ""));
        std::set<std::string> known;
        for (const auto &url : inbox_)
            known.insert(link_key(url));
        for (const auto &item : items_)
            known.insert(link_key(item["url"]));
        int added = 0;
        for (const auto &url : links)
            if (qualifies(url, settings_) && known.insert(link_key(url)).second)
            {
                inbox_.push_back(url);
                ++added;
            }
        if (added)
            save_inbox();
        result["added"] = added;
    }
    else if (op == "inbox.clear")
    {
        inbox_ = json::array();
        save_inbox();
    }
    else if (op == "inbox.remove" || op == "inbox.queue")
    {
        const auto selected = r.at("urls").get<std::vector<std::string>>();
        std::vector<std::string> found;
        for (const auto &url : inbox_)
            if (std::find(selected.begin(), selected.end(), url.get<std::string>()) != selected.end())
                found.push_back(url);
        if (op == "inbox.queue")
            result = enqueue(found, r.value("mode", settings_["defaultMode"].get<std::string>()));
        inbox_.erase(std::remove_if(inbox_.begin(), inbox_.end(),
                                    [&](const auto &url) {
                                        return std::find(found.begin(), found.end(), url.template get<std::string>()) !=
                                               found.end();
                                    }),
                     inbox_.end());
        save_inbox();
    }
    else if (op == "queue.start")
        running_ = true;
    else if (op == "queue.pause")
        running_ = false;
    else if (op == "queue.cancelAll" || op == "queue.cancel" || op == "queue.remove")
    {
        const auto id = r.value("id", "");
        for (auto &item : items_)
            if (op == "queue.cancelAll" || item["id"] == id)
            {
                if (item["status"] == "Queued")
                    item["status"] = "Canceled";
                if (item["status"] == "Running")
                    for (auto &job : jobs_)
                        if (item["id"] == job->id)
                            *job->stop = true;
            }
        if (op == "queue.remove")
            items_.erase(
                std::remove_if(items_.begin(), items_.end(), [&](const auto &item) { return item["id"] == id; }),
                items_.end());
        save_queue();
    }
    else if (op == "queue.retry" || op == "queue.retryFailed")
    {
        const auto id = r.value("id", "");
        for (auto &item : items_)
        {
            auto status = item["status"].get<std::string>();
            if ((op == "queue.retryFailed" ? (status == "Failed" || status == "Canceled")
                                           : item["id"] == id && status != "Running") &&
                status != "Queued")
                item = new_item(item["id"], item["url"], item["mode"]);
        }
        save_queue();
        running_ = true;
    }
    else if (op == "queue.clearFinished")
    {
        items_.erase(std::remove_if(items_.begin(), items_.end(),
                                    [](const auto &item) {
                                        return item["status"] == "Completed" || item["status"] == "Canceled";
                                    }),
                     items_.end());
        save_queue();
    }
    else if (op == "tools.check")
        start_tools(true, false);
    else if (op == "tools.update")
        start_tools(true, true, r.value("tool", ""));
    else if (op == "tools.cancel")
    {
        if (updater_)
            *updater_->stop = true;
    }
    else if (op == "folder.prepare")
    {
        auto folder =
            r.value("kind", "") == "tools" ? tools_path(settings_, home_) : path(settings_["outputDirectory"]);
        fs::create_directories(folder);
        result["path"] = path_text(folder);
    }
    else
        throw std::runtime_error("Unknown engine operation: " + op);
    ++revision_;
    wake_.notify_all();
    result["state"] = snapshot();
    return result;
}
void Engine::schedule()
{
    std::unique_lock lock(mutex_);
    while (!stopping_)
    {
        try
        {
            for (auto i = jobs_.begin(); i != jobs_.end();)
                if ((*i)->done)
                {
                    (*i)->thread.join();
                    i = jobs_.erase(i);
                }
                else
                    ++i;
            if (initialized_ && !tools_busy_ && Clock::now() >= next_check_)
            {
                next_check_ = Clock::now() + std::chrono::hours(24);
                if (settings_["checkForToolUpdatesOnStartup"] == true)
                    start_tools(true, settings_["installToolUpdatesAutomatically"]);
            }
            if (running_ && !tools_busy_)
            {
                const auto directory = tools_path(settings_, home_);
                const bool queued =
                    std::any_of(items_.begin(), items_.end(), [](const auto &i) { return i["status"] == "Queued"; });
                if (queued && !tools_ready(directory))
                {
                    running_ = false;
                    notify("A required tool is missing. Install yt-dlp, Deno, and FFmpeg/ffprobe, then press Start.");
                    if (settings_["installToolUpdatesAutomatically"] == true)
                        start_tools(true, true);
                }
                else
                    for (auto &item : items_)
                    {
                        if (!running_ || active() >= settings_["concurrency"].get<size_t>())
                            break;
                        if (item["status"] == "Queued")
                            start_download(item);
                    }
            }
        }
        catch (const std::exception &ex)
        {
            running_ = false;
            notify(ex.what());
        }
        wake_.wait_for(lock, std::chrono::milliseconds(150));
    }
}
void Engine::start_download(json &item)
{
    const auto settings = settings_;
    const auto directory = tools_path(settings, home_);
    const auto output = path(settings["outputDirectory"]);
    fs::create_directories(output);
    ensure_disk(output);
    const auto arguments = download_arguments(settings, directory, item["mode"], item["url"]);
    auto job = std::make_unique<Job>();
    job->id = item["id"];
    job->stop = std::make_shared<std::atomic_bool>(false);
    auto ptr = job.get();
    item["status"] = "Running";
    item["indeterminate"] = true;
    ++revision_;
    ptr->thread = std::thread([this, ptr, directory, output, arguments] {
        std::string status = "Completed", error;
        try
        {
            auto exit = run_process(
                directory / L"yt-dlp.exe", arguments, directory, *ptr->stop,
                [&](const auto &line, bool err) {
                    std::lock_guard lock(mutex_);
                    if (auto item = find_item(ptr->id))
                    {
                        apply_output(*item, line, err);
                        ++revision_;
                    }
                },
                output);
            if (exit)
            {
                status = "Failed";
                error = "yt-dlp exited with code " + std::to_string(exit) + ".";
            }
        }
        catch (const Canceled &)
        {
            status = "Canceled";
        }
        catch (const std::exception &ex)
        {
            status = "Failed";
            error = ex.what();
        }
        {
            std::lock_guard lock(mutex_);
            if (auto item = find_item(ptr->id))
            {
                (*item)["status"] = status;
                (*item)["indeterminate"] = false;
                (*item)["speed"] = nullptr;
                (*item)["eta"] = nullptr;
                if (status == "Completed")
                {
                    (*item)["progress"] = 100.0;
                    (*item)["stage"] = "";
                    (*item)["error"] = "";
                }
                else if (!error.empty() && (*item)["error"] == "")
                    (*item)["error"] = error;
                if (error.find("Low disk space") != std::string::npos)
                {
                    running_ = false;
                    notify(error);
                }
            }
            try
            {
                save_queue();
            }
            catch (const std::exception &ex)
            {
                notify(ex.what());
            }
            ++revision_;
        }
        ptr->done = true;
        wake_.notify_all();
    });
    jobs_.push_back(std::move(job));
}
void Engine::start_tools(bool check, bool install, const std::string &only)
{
    if (tools_busy_)
        return;
    if (!only.empty())
        (void)executable_name(only);
    if (updater_ && updater_->thread.joinable())
        updater_->thread.join();
    updater_ = std::make_unique<Job>();
    updater_->stop = std::make_shared<std::atomic_bool>(false);
    tools_busy_ = true;
    status_ = active() ? "Waiting for downloads to finish" : "Checking tools";
    ++revision_;
    const auto settings = settings_;
    const auto stop = updater_->stop;
    updater_->thread = std::thread(
        [this, check, install, only, settings, stop] { tools_worker(check, install, only, settings, stop); });
}
void Engine::tools_worker(bool check, bool install, std::string only, json settings,
                          std::shared_ptr<std::atomic_bool> stop)
{
    auto update = [&](size_t i, const std::function<void(json &)> &f) {
        std::lock_guard lock(mutex_);
        f(tools_[i]);
        ++revision_;
    };
    try
    {
        {
            std::unique_lock lock(mutex_);
            wake_.wait(lock, [&] { return stopping_ || *stop || active() == 0; });
            canceled(*stop);
            if (stopping_)
                throw Canceled();
        }
        const auto directory = tools_path(settings, home_);
        for (size_t i = 0; i < 3; ++i)
        {
            const std::string tool = i == 0 ? "yt-dlp" : i == 1 ? "deno" : "ffmpeg";
            if (!only.empty() && only != tool)
                continue;
            update(i, [&](auto &t) {
                t["working"] = true;
                t["activity"] = "Checking";
                t["error"] = "";
                t["progress"] = -1.0;
            });
            try
            {
                canceled(*stop);
                auto local = local_version(directory, tool, *stop);
                update(i, [&](auto &t) { t["installed"] = local; });
                if (check || (install && local.empty()))
                {
                    auto release = latest_release(tool, settings["ytDlpChannel"], *stop);
                    update(i, [&](auto &t) { t["latest"] = release["version"]; });
                    if (install && (release["version"] != local || !only.empty()))
                    {
                        {
                            std::lock_guard lock(mutex_);
                            status_ = "Installing " + tool;
                            log(status_);
                        }
                        install_tool(directory, tool, release, *stop, [&](const auto &stage, double fraction) {
                            update(i, [&](auto &t) {
                                t["activity"] = stage;
                                t["progress"] = fraction;
                            });
                        });
                        local = local_version(directory, tool, *stop);
                        update(i, [&](auto &t) { t["installed"] = local; });
                        std::lock_guard lock(mutex_);
                        log(tool + ": installed " + local);
                    }
                }
            }
            catch (const Canceled &)
            {
                throw;
            }
            catch (const std::exception &ex)
            {
                update(i, [&](auto &t) { t["error"] = ex.what(); });
                std::lock_guard lock(mutex_);
                log(tool + ": " + ex.what());
                notify(tool + ": " + ex.what());
            }
            update(i, [](auto &t) { t["working"] = false; });
        }
    }
    catch (const Canceled &)
    {
        std::lock_guard lock(mutex_);
        log("Tool operation canceled.");
    }
    catch (const std::exception &ex)
    {
        std::lock_guard lock(mutex_);
        notify(ex.what());
    }
    {
        std::lock_guard lock(mutex_);
        for (auto &t : tools_)
            t["working"] = false;
        tools_busy_ = false;
        status_.clear();
        ++revision_;
    }
    wake_.notify_all();
}
} // namespace ovm
