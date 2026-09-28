#pragma once
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <filesystem>
#include <functional>
#include <map>
#include <memory>
#include <mutex>
#include <nlohmann/json.hpp>
#include <string>
#include <thread>
#include <utility>
#include <vector>
#include <windows.h>

namespace ovm
{
using json = nlohmann::json;
namespace fs = std::filesystem;
using Clock = std::chrono::steady_clock;
inline constexpr uint64_t disk_reserve = 2ull * 1024 * 1024 * 1024;
struct Canceled : std::runtime_error
{
    Canceled() : runtime_error("Operation canceled.") {}
};
struct RollbackFailed : std::runtime_error
{
    using std::runtime_error::runtime_error;
};
struct Handle
{
    HANDLE value = nullptr;
    explicit Handle(HANDLE h = nullptr) : value(h) {}
    ~Handle()
    {
        if (value && value != INVALID_HANDLE_VALUE)
            CloseHandle(value);
    }
    Handle(const Handle &) = delete;
    Handle &operator=(const Handle &) = delete;
    Handle(Handle &&other) noexcept : value(std::exchange(other.value, nullptr)) {}
    HANDLE release()
    {
        return std::exchange(value, nullptr);
    }
    explicit operator bool() const
    {
        return value && value != INVALID_HANDLE_VALUE;
    }
};
std::wstring wide(const std::string &s);
std::string utf8(const std::wstring &s);
std::string path_text(const fs::path &p);
fs::path path(const std::string &s);
std::string trim(std::string s);
std::string lower(std::string s);
std::string error_text(const std::string &what);
std::wstring quote_argument(const std::wstring &value);
void canceled(const std::atomic_bool &stop);
fs::path default_home();
fs::path default_output();
std::string read_text(const fs::path &file, size_t limit = 16 * 1024 * 1024);
void write_json(const fs::path &file, const json &value);
json read_json(const fs::path &file, const json &fallback);
void ensure_disk(const fs::path &directory, uint64_t reserve = disk_reserve);
using LineCallback = std::function<void(const std::string &, bool)>;
int run_process(const fs::path &executable, const std::vector<std::string> &arguments, const fs::path &directory,
                std::atomic_bool &stop, const LineCallback &line, const fs::path &output = {},
                std::chrono::seconds timeout = std::chrono::hours(24));
std::string capture(const fs::path &executable, const std::vector<std::string> &arguments, std::atomic_bool &stop);
using TransferProgress = std::function<void(uint64_t, uint64_t)>;
std::string http_get(const std::string &url, std::atomic_bool &stop);
void http_download(const std::string &url, const fs::path &file, std::atomic_bool &stop,
                   const TransferProgress &progress);
std::string sha256(const fs::path &file);
std::string checksum_for(const std::string &sums, const std::string &name);
std::vector<fs::path> extract_zip(const fs::path &archive, const fs::path &out, const std::string &tool,
                                  std::atomic_bool &stop);
json default_settings();
json normalize_settings(json input);
std::vector<std::string> known_sites();
std::vector<std::string> extract_links(const std::string &text, const std::string &html = "");
std::string link_key(const std::string &url);
bool qualifies(const std::string &url, const json &settings);
std::vector<std::string> download_arguments(const json &settings, const fs::path &tools, const std::string &mode,
                                            const std::string &url);
void apply_output(json &item, const std::string &line, bool error);
json new_item(const std::string &id, const std::string &url, const std::string &mode);
fs::path tools_path(const json &settings, const fs::path &home);
std::string executable_name(const std::string &tool);
bool tools_ready(const fs::path &directory);
json latest_release(const std::string &tool, const std::string &channel, std::atomic_bool &stop);
std::string local_version(const fs::path &directory, const std::string &tool, std::atomic_bool &stop);
using InstallProgress = std::function<void(const std::string &, double)>;
void install_tool(const fs::path &directory, const std::string &tool, const json &release, std::atomic_bool &stop,
                  const InstallProgress &progress);
void commit_tool_files(const fs::path &directory, const fs::path &staging, const std::string &tool, const json &release,
                       const std::vector<fs::path> &files, std::atomic_bool &stop);

class Engine
{
  public:
    explicit Engine(fs::path home);
    ~Engine();
    json execute(const json &request);

  private:
    struct Job
    {
        std::string id;
        std::shared_ptr<std::atomic_bool> stop;
        std::thread thread;
        std::atomic_bool done{false};
    };
    fs::path home_;
    Handle ownership_;
    std::mutex mutex_;
    std::condition_variable wake_;
    std::atomic_bool stopping_{false};
    std::thread scheduler_;
    std::vector<std::unique_ptr<Job>> jobs_;
    std::unique_ptr<Job> updater_;
    json settings_, items_ = json::array(), inbox_ = json::array(), tools_ = json::array(), logs_ = json::array();
    bool running_ = false, tools_busy_ = false, initialized_ = false;
    uint64_t next_id_ = 1, revision_ = 0, notice_id_ = 0;
    std::string notice_, status_;
    Clock::time_point next_check_ = Clock::now() + std::chrono::hours(24);
    void schedule();
    void save_queue();
    void save_inbox();
    void notify(const std::string &message);
    void log(const std::string &message);
    json snapshot() const;
    json *find_item(const std::string &id);
    size_t active() const;
    void start_download(json &item);
    void start_tools(bool check, bool install, const std::string &only = "");
    void tools_worker(bool check, bool install, std::string only, json settings,
                      std::shared_ptr<std::atomic_bool> stop);
    json enqueue(const std::vector<std::string> &links, const std::string &mode);
};
} // namespace ovm
