#include "internal.hpp"
#include "ovm.h"
#include <fstream>
#include <iostream>
#include <limits>
#include <miniz.h>

int main(int argc, char **argv)
{
    std::cout << std::unitbuf;
    using namespace ovm;
    int passed = 0;
    auto check = [&](bool value, const char *name) {
        if (!value)
            throw std::runtime_error(name);
        ++passed;
        std::cout << "PASS " << name << '\n';
    };
    const auto root = fs::temp_directory_path() / (L"ovm-native-tests-" + std::to_wstring(GetCurrentProcessId()));
    fs::create_directories(root);
    try
    {
        if (argc != 2)
            throw std::runtime_error("Missing test process executable.");
        const auto helper = path(argv[1]);
        check(ovm_abi_version() == 2, "C ABI version");
        check(utf8(wide("日本語 🎵")) == "日本語 🎵", "UTF-8 round trip");
        check(quote_argument(L"a b\\") == L"\"a b\\\\\"", "Windows trailing slash quoting");
        check(link_key("https://youtu.be/abcdefghijk?si=test") ==
                  link_key("https://www.youtube.com/watch?v=abcdefghijk&t=1"),
              "YouTube canonical duplicates");
        check(extract_links("(https://vimeo.com/1). https://vimeo.com/1").size() == 1, "Extraction and deduplication");
        check(extract_links("", "<a href=\"https://example.org/?a=1&amp;b=2\">x</a>")[0] ==
                  "https://example.org/?a=1&b=2",
              "HTML clipboard entities");
        auto settings = normalize_settings({{"concurrency", 99}, {"videoCodec", "av1"}});
        check(settings["concurrency"] == 8 && settings["videoCodec"] == "Av1", "Native settings normalization");
        check(!qualifies("https://youtube.com.evil.test/watch?v=abcdefghijk", settings),
              "Domain suffix attack rejected");
        check(!qualifies("https://youtube.com/results?search_query=x", settings), "YouTube navigation excluded");
        check(qualifies("https://youtube.com/watch?v=abcdefghijk", settings), "Video link qualifies");
        auto args = download_arguments(settings, root, "Video", "https://vimeo.com/1");
        check(std::find(args.begin(), args.end(), "bv*[vcodec~='^(av01)']+ba/b[vcodec~='^(av01)']") != args.end(),
              "Native codec selection");
        check(args[args.size() - 2] == "--", "URL option boundary");
        auto item = new_item("1", "https://vimeo.com/1", "Video");
        apply_output(item, "@@P 50|100|NA|25|2", false);
        check(item["progress"] == 50 && item["speed"] == 25, "Native progress parsing");
        apply_output(item, "[info] x: Downloading 1 format(s): 399+251", false);
        apply_output(item, "[download] Destination: a", false);
        apply_output(item, "@@P 100|100|NA|25|2", false);
        check(item["progress"] == 50, "Multistream aggregate progress");
        const std::string digest(64, 'a');
        check(checksum_for(digest + "  file.zip\n", "file.zip") == digest, "Checksum lookup");
        check(checksum_for("Algorithm : SHA256\r\nHash : " + digest + "\r\nPath : C:\\build\\deno.zip\r\n",
                           "deno.zip") == digest,
              "Deno Windows checksum format");
        bool rejected = false;
        try
        {
            checksum_for(digest + "  other.zip", "file.zip");
        }
        catch (...)
        {
            rejected = true;
        }
        check(rejected, "Missing checksum rejected");
        {
            std::ofstream f(root / L"hash.txt");
            f << "abc";
        }
        check(sha256(root / L"hash.txt") == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
              "SHA-256 known vector");
        rejected = false;
        try
        {
            ensure_disk(root, std::numeric_limits<uint64_t>::max());
        }
        catch (...)
        {
            rejected = true;
        }
        check(rejected, "Disk reserve enforced");
        std::atomic_bool stop{false};
        DWORD child_pid = 0;
        rejected = false;
        try
        {
            run_process(helper, {"--tree"}, root, stop, [&](const auto &line, bool) {
                child_pid = std::stoul(line);
                stop = true;
            });
        }
        catch (const Canceled &)
        {
            rejected = true;
        }
        check(rejected && child_pid != 0, "Native process cancellation");
        Handle child(OpenProcess(SYNCHRONIZE, FALSE, child_pid));
        check(!child || WaitForSingleObject(child.value, 0) == WAIT_OBJECT_0,
              "Cancellation waits for descendant process exit");
        stop = false;
        std::vector<std::string> lines;
        check(run_process(helper, {"https://fixture.test/ok"}, root, stop,
                          [&](const auto &s, bool) { lines.push_back(s); }) == 0 &&
                  lines.size() == 2,
              "Native process output capture");
        write_json(root / L"settings.json", {{"startQueueAutomatically", true},
                                             {"installToolUpdatesAutomatically", false},
                                             {"checkForToolUpdatesOnStartup", false}});
        {
            Engine engine(root);
            auto collected =
                engine.execute({{"operation", "clipboard.collect"}, {"text", "https://vimeo.com/1"}}).at("state");
            check(collected["items"].empty() && collected["inbox"].size() == 1 && collected["running"] == false,
                  "Clipboard cannot start downloads");
            auto settings2 = collected["settings"];
            settings2["startQueueAutomatically"] = false;
            engine.execute({{"operation", "settings.set"}, {"settings", settings2}});
            auto added = engine.execute(
                {{"operation", "queue.add"}, {"text", "https://vimeo.com/2 https://vimeo.com/2"}, {"mode", "Audio"}});
            check(added["added"] == 1 && added["state"]["running"] == false, "Queue added paused in native engine");
            auto id = added["state"]["items"][0]["id"];
            auto canceled = engine.execute({{"operation", "queue.cancel"}, {"id", id}});
            check(canceled["state"]["items"][0]["status"] == "Canceled", "Native cancellation state");
            rejected = false;
            try
            {
                Engine second(root);
            }
            catch (...)
            {
                rejected = true;
            }
            check(rejected, "Data directory ownership exclusive");
        }
        {
            Engine restored(root);
            auto state = restored.execute({{"operation", "snapshot"}})["state"];
            check(state["inbox"].size() == 1 && state["items"].size() == 1 && state["running"] == false,
                  "Native persistence and paused restore");
        }
        auto invalid = ovm_execute(99999, "{}");
        check(json::parse(invalid).contains("error"), "ABI invalid handle safe");
        ovm_free(invalid);
        auto opened = ovm_open(path_text(root).c_str());
        auto handle = json::parse(opened).at("handle").get<uint64_t>();
        ovm_free(opened);
        auto malformed = ovm_execute(handle, "not json");
        check(json::parse(malformed).contains("error"), "ABI malformed JSON safe");
        ovm_free(malformed);
        ovm_close(handle);
        ovm_close(handle);
        const auto fake = root / L"fake-tools";
        fs::create_directories(fake);
        for (const auto &name : {L"yt-dlp.exe", L"deno.exe", L"ffmpeg.exe", L"ffprobe.exe"})
            fs::copy_file(helper, fake / name, fs::copy_options::overwrite_existing);
        auto staging = fake / L"test-staging";
        fs::create_directories(staging / L"out");
        const auto old_hash = sha256(fake / L"yt-dlp.exe");
        {
            std::ofstream invalid(staging / L"out" / L"yt-dlp.exe");
            invalid << "not an executable";
        }
        rejected = false;
        try
        {
            commit_tool_files(fake, staging, "yt-dlp", {{"version", "bad"}}, {staging / L"out" / L"yt-dlp.exe"}, stop);
        }
        catch (const std::exception &)
        {
            rejected = true;
        }
        check(rejected && sha256(fake / L"yt-dlp.exe") == old_hash,
              "Failed executable validation rolls back original tool");
        check(!fs::exists(fake / L".ovm-tools.json"), "Failed installation does not commit manifest");
        auto make_zip = [&](const fs::path &file, const std::vector<std::string> &names) {
            mz_zip_archive archive{};
            if (!mz_zip_writer_init_heap(&archive, 0, 0))
                throw std::runtime_error("ZIP fixture initialization");
            for (const auto &name : names)
                if (!mz_zip_writer_add_mem(&archive, name.c_str(), "abc", 3, 0))
                    throw std::runtime_error("ZIP fixture entry");
            void *memory = nullptr;
            size_t size = 0;
            if (!mz_zip_writer_finalize_heap_archive(&archive, &memory, &size))
                throw std::runtime_error("ZIP fixture finish");
            std::ofstream output(file, std::ios::binary);
            output.write(static_cast<const char *>(memory), size);
            mz_free(memory);
            mz_zip_writer_end(&archive);
        };
        make_zip(root / L"valid.zip", {"release/bin/ffmpeg.exe", "release/bin/ffprobe.exe", "release/bin/avcodec.dll",
                                       "release/doc/readme.txt"});
        auto extracted = extract_zip(root / L"valid.zip", root / L"unpacked", "ffmpeg", stop);
        check(extracted.size() == 3 && fs::exists(root / L"unpacked" / L"ffprobe.exe"),
              "FFmpeg ZIP includes ffprobe and shared libraries");
        make_zip(root / L"duplicate.zip", {"a/bin/ffmpeg.exe", "b/bin/ffmpeg.exe"});
        rejected = false;
        try
        {
            extract_zip(root / L"duplicate.zip", root / L"duplicates", "ffmpeg", stop);
        }
        catch (...)
        {
            rejected = true;
        }
        check(rejected, "Duplicate archive destinations rejected");
        make_zip(root / L"unsafe.zip", {"release/bin/bad:stream"});
        rejected = false;
        try
        {
            extract_zip(root / L"unsafe.zip", root / L"unsafe", "ffmpeg", stop);
        }
        catch (...)
        {
            rejected = true;
        }
        check(rejected, "Archive NTFS stream names rejected");
        const auto queue_home = root / L"scheduler";
        write_json(queue_home / L"settings.json", {{"toolsDirectory", path_text(fake)},
                                                   {"outputDirectory", path_text(root / L"output")},
                                                   {"concurrency", 1},
                                                   {"checkForToolUpdatesOnStartup", false},
                                                   {"installToolUpdatesAutomatically", false}});
        {
            Engine engine(queue_home);
            auto wait = [&](const auto &predicate) {
                auto deadline = Clock::now() + std::chrono::seconds(10);
                while (Clock::now() < deadline)
                {
                    auto state = engine.execute({{"operation", "snapshot"}})["state"];
                    if (predicate(state))
                        return state;
                    Sleep(20);
                }
                throw std::runtime_error("Timed out waiting for native scheduler.");
            };
            engine.execute(
                {{"operation", "queue.add"}, {"text", "https://fixture.test/slow1 https://fixture.test/slow2"}});
            engine.execute({{"operation", "queue.start"}});
            auto state = wait([](const auto &s) { return s["items"][0]["status"] == "Running"; });
            check(state["items"][1]["status"] == "Queued", "Native concurrency limit");
            engine.execute({{"operation", "initialize"}});
            state = wait([](const auto &s) { return s["toolsBusy"] == true; });
            check(state["toolStatus"] == "Waiting for downloads to finish",
                  "Tool operations coordinate with active downloads");
            engine.execute({{"operation", "queue.remove"}, {"id", state["items"][0]["id"]}});
            state = wait([](const auto &s) { return s["toolsBusy"] == false && s["items"][0]["status"] == "Running"; });
            check(state["items"].size() == 1, "Removing active item cancels before tool scan and queue resume");
            engine.execute({{"operation", "queue.cancelAll"}});
            wait([](const auto &s) { return s["items"][0]["status"] == "Canceled"; });
            engine.execute({{"operation", "queue.add"}, {"text", "https://fixture.test/fail"}});
            state = wait([](const auto &s) { return s["items"][1]["status"] == "Failed"; });
            check(state["items"][1]["error"] == "ERROR: fixture failure", "Native process failure surfaces stderr");
        }
        std::cout << passed << " native tests passed.\n";
    }
    catch (const std::exception &ex)
    {
        std::cerr << "FAIL: " << ex.what() << '\n';
        return 1;
    }
    std::error_code ec;
    fs::remove_all(root, ec);
    return 0;
}
