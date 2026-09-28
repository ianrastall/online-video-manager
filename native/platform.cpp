#include "internal.hpp"
#include <algorithm>
#include <array>
#include <bcrypt.h>
#include <fstream>
#include <iomanip>
#include <miniz.h>
#include <shlobj.h>
#include <sstream>
#include <winhttp.h>

namespace ovm
{
std::wstring wide(const std::string &s)
{
    if (s.empty())
        return {};
    const int n = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, s.data(), static_cast<int>(s.size()), nullptr, 0);
    if (!n)
        throw std::runtime_error("Invalid UTF-8 input.");
    std::wstring out(n, 0);
    MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, s.data(), static_cast<int>(s.size()), out.data(), n);
    return out;
}
std::string utf8(const std::wstring &s)
{
    if (s.empty())
        return {};
    int n = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, s.data(), static_cast<int>(s.size()), nullptr, 0,
                                nullptr, nullptr);
    if (!n)
        throw std::runtime_error("Invalid UTF-16 input.");
    std::string out(n, 0);
    WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, s.data(), static_cast<int>(s.size()), out.data(), n, nullptr,
                        nullptr);
    return out;
}
std::string path_text(const fs::path &p)
{
    return utf8(p.wstring());
}
fs::path path(const std::string &s)
{
    return fs::path(wide(s));
}
std::string trim(std::string s)
{
    auto a = s.find_first_not_of(" \t\r\n");
    return a == std::string::npos ? "" : s.substr(a, s.find_last_not_of(" \t\r\n") - a + 1);
}
std::string lower(std::string s)
{
    for (auto &c : s)
        c = static_cast<char>(std::tolower(static_cast<unsigned char>(c)));
    return s;
}
std::string error_text(const std::string &what)
{
    return what + " (Windows error " + std::to_string(GetLastError()) + ").";
}
void canceled(const std::atomic_bool &stop)
{
    if (stop)
        throw Canceled();
}
static fs::path known_folder(REFKNOWNFOLDERID id)
{
    PWSTR p = nullptr;
    if (FAILED(SHGetKnownFolderPath(id, 0, nullptr, &p)))
        throw std::runtime_error("Cannot locate the Windows user folder.");
    fs::path result(p);
    CoTaskMemFree(p);
    return result;
}
fs::path default_home()
{
    std::array<wchar_t, 32768> buffer{};
    DWORD n = GetEnvironmentVariableW(L"OVM_HOME", buffer.data(), static_cast<DWORD>(buffer.size()));
    return n && n < buffer.size() ? fs::path(buffer.data())
                                  : known_folder(FOLDERID_LocalAppData) / L"OnlineVideoManager";
}
fs::path default_output()
{
    return fs::path(L"D:\\all");
}
std::string read_text(const fs::path &file, size_t limit)
{
    if (fs::file_size(file) > limit)
        throw std::runtime_error("Text file exceeds the supported size limit.");
    std::ifstream in(file, std::ios::binary);
    if (!in)
        throw std::runtime_error("Cannot open " + path_text(file));
    std::string text((std::istreambuf_iterator<char>(in)), {});
    if (text.starts_with("\xEF\xBB\xBF"))
        text.erase(0, 3);
    else if (text.size() >= 2 && static_cast<unsigned char>(text[0]) == 0xff &&
             static_cast<unsigned char>(text[1]) == 0xfe)
    {
        if (text.size() % 2)
            throw std::runtime_error("Invalid UTF-16 text file.");
        std::wstring w;
        for (size_t i = 2; i < text.size(); i += 2)
            w += static_cast<wchar_t>(static_cast<unsigned char>(text[i]) |
                                      (static_cast<unsigned char>(text[i + 1]) << 8));
        text = utf8(w);
    }
    return text;
}
void write_json(const fs::path &file, const json &value)
{
    fs::create_directories(file.parent_path());
    auto temp = file;
    temp += L".tmp";
    {
        std::ofstream out(temp, std::ios::binary | std::ios::trunc);
        const auto data = value.dump(2, ' ', false, json::error_handler_t::replace);
        out.write(data.data(), static_cast<std::streamsize>(data.size()));
        out.flush();
        if (!out)
            throw std::runtime_error("Cannot save " + path_text(file));
    }
    if (!MoveFileExW(temp.c_str(), file.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH))
        throw std::runtime_error(error_text("Cannot replace " + path_text(file)));
}
json read_json(const fs::path &file, const json &fallback)
{
    if (!fs::exists(file))
        return fallback;
    try
    {
        return json::parse(read_text(file));
    }
    catch (const json::exception &)
    {
        auto backup = file;
        backup += L".invalid." + std::to_wstring(GetTickCount64());
        fs::rename(file, backup); // Preserve corrupt input rather than overwrite it silently.
        return fallback;
    }
}
void ensure_disk(const fs::path &directory, uint64_t reserve)
{
    auto existing = fs::absolute(directory);
    while (!fs::exists(existing) && existing.has_parent_path() && existing != existing.parent_path())
        existing = existing.parent_path();
    ULARGE_INTEGER available{}, total{}, free{};
    if (!GetDiskFreeSpaceExW(existing.c_str(), &available, &total, &free))
        throw std::runtime_error(error_text("Cannot check free disk space"));
    if (available.QuadPart < reserve)
        throw std::runtime_error("Low disk space: OVM needs at least " + std::to_string(reserve / 1024 / 1024) +
                                 " MiB free. Choose another folder or free space, then retry.");
}
std::wstring quote_argument(const std::wstring &value)
{
    std::wstring out = L"\"";
    size_t slashes = 0;
    for (wchar_t c : value)
    {
        if (c == L'\\')
        {
            ++slashes;
            continue;
        }
        out.append(c == L'"' ? slashes * 2 + 1 : slashes, L'\\');
        slashes = 0;
        out += c;
    }
    out.append(slashes * 2, L'\\');
    return out + L'"';
}
int run_process(const fs::path &executable, const std::vector<std::string> &arguments, const fs::path &directory,
                std::atomic_bool &stop, const LineCallback &line, const fs::path &output, std::chrono::seconds timeout)
{
    canceled(stop);
    // Child tools can leave the MSIX file-system view. MSVC canonical() resolves the
    // opened file through GetFinalPathNameByHandleW, so their loader (and yt-dlp's
    // self-extractor) can find the executable and its adjacent DLLs on disk.
    const auto executable_path = fs::canonical(executable);
    const auto working_directory = directory.empty() ? fs::path{} : fs::canonical(directory);
    // Damaged tools must produce an error, never a Windows loader dialog on a worker thread.
    const DWORD old_mode = GetThreadErrorMode();
    if (!SetThreadErrorMode(old_mode | SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX,
                            nullptr))
        throw std::runtime_error(error_text("Cannot configure process error handling"));
    struct ErrorModeGuard
    {
        DWORD mode;
        ~ErrorModeGuard()
        {
            SetThreadErrorMode(mode, nullptr);
        }
    } error_mode{old_mode};
    DWORD binary_type = 0;
    if (!GetBinaryTypeW(executable_path.c_str(), &binary_type) || binary_type != SCS_64BIT_BINARY)
        throw std::runtime_error("Tool is not a valid Windows x64 executable: " + path_text(executable));
    if (!output.empty())
        ensure_disk(output);
    SECURITY_ATTRIBUTES sa{sizeof(sa), nullptr, TRUE};
    HANDLE ro{}, wo{}, re{}, we{};
    if (!CreatePipe(&ro, &wo, &sa, 0))
        throw std::runtime_error(error_text("Cannot create output pipe"));
    Handle stdout_read(ro), stdout_write(wo);
    if (!CreatePipe(&re, &we, &sa, 0))
        throw std::runtime_error(error_text("Cannot create error pipe"));
    Handle stderr_read(re), stderr_write(we);
    SetHandleInformation(ro, HANDLE_FLAG_INHERIT, 0);
    SetHandleInformation(re, HANDLE_FLAG_INHERIT, 0);
    Handle input(CreateFileW(L"NUL", GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, &sa, OPEN_EXISTING, 0, nullptr));
    if (!input)
        throw std::runtime_error(error_text("Cannot open process input"));
    Handle job(CreateJobObjectW(nullptr, nullptr));
    JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits{};
    limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
    if (!job || !SetInformationJobObject(job.value, JobObjectExtendedLimitInformation, &limits, sizeof(limits)))
        throw std::runtime_error(error_text("Cannot create process job"));
    SIZE_T bytes = 0;
    InitializeProcThreadAttributeList(nullptr, 1, 0, &bytes);
    std::vector<unsigned char> attribute_data(bytes);
    auto attributes = reinterpret_cast<LPPROC_THREAD_ATTRIBUTE_LIST>(attribute_data.data());
    if (!InitializeProcThreadAttributeList(attributes, 1, 0, &bytes))
        throw std::runtime_error(error_text("Cannot initialize process attributes"));
    struct AttributeCleanup
    {
        LPPROC_THREAD_ATTRIBUTE_LIST p;
        ~AttributeCleanup()
        {
            DeleteProcThreadAttributeList(p);
        }
    } attr_cleanup{attributes};
    HANDLE inherited[] = {wo, we, input.value};
    if (!UpdateProcThreadAttribute(attributes, 0, PROC_THREAD_ATTRIBUTE_HANDLE_LIST, inherited, sizeof(inherited),
                                   nullptr, nullptr))
        throw std::runtime_error(error_text("Cannot constrain inherited handles"));
    STARTUPINFOEXW si{};
    si.StartupInfo.cb = sizeof(si);
    si.lpAttributeList = attributes;
    si.StartupInfo.dwFlags = STARTF_USESTDHANDLES;
    si.StartupInfo.hStdInput = input.value;
    si.StartupInfo.hStdOutput = wo;
    si.StartupInfo.hStdError = we;
    std::wstring command = quote_argument(executable_path.wstring());
    for (const auto &a : arguments)
        command += L" " + quote_argument(wide(a));
    if (command.size() >= 32767)
        throw std::runtime_error("Download command exceeds the Windows command line limit.");
    PROCESS_INFORMATION pi{};
    if (!CreateProcessW(executable_path.c_str(), command.data(), nullptr, nullptr, TRUE,
                        CREATE_NO_WINDOW | CREATE_SUSPENDED | EXTENDED_STARTUPINFO_PRESENT, nullptr,
                        working_directory.empty() ? nullptr : working_directory.c_str(), &si.StartupInfo, &pi))
        throw std::runtime_error(error_text("Cannot start " + path_text(executable)));
    Handle process(pi.hProcess), thread(pi.hThread);
    if (!AssignProcessToJobObject(job.value, pi.hProcess))
    {
        TerminateProcess(pi.hProcess, 1);
        throw std::runtime_error(error_text("Cannot contain process tree"));
    }
    if (ResumeThread(pi.hThread) == static_cast<DWORD>(-1))
    {
        TerminateJobObject(job.value, 1);
        throw std::runtime_error(error_text("Cannot resume process"));
    }
    CloseHandle(stdout_write.release());
    CloseHandle(stderr_write.release());
    std::mutex callback_mutex;
    std::exception_ptr callback_error;
    auto pump = [&](HANDLE h, bool err) {
        try
        {
            std::array<char, 8192> buffer{};
            DWORD n = 0;
            std::string pending;
            auto emit = [&] {
                if (!pending.empty())
                {
                    std::lock_guard guard(callback_mutex);
                    line(pending, err);
                    pending.clear();
                }
            };
            while (ReadFile(h, buffer.data(), static_cast<DWORD>(buffer.size()), &n, nullptr) && n)
            {
                for (DWORD i = 0; i < n; ++i)
                {
                    if (buffer[i] == '\n' || buffer[i] == '\r')
                        emit();
                    else if (pending.size() < 1024 * 1024)
                        pending += buffer[i];
                }
            }
            emit();
        }
        catch (...)
        {
            std::lock_guard guard(callback_mutex);
            callback_error = std::current_exception();
            TerminateJobObject(job.value, 1);
        }
    };
    std::thread out_thread, err_thread;
    try
    {
        out_thread = std::thread(pump, ro, false);
        err_thread = std::thread(pump, re, true);
    }
    catch (...)
    {
        TerminateJobObject(job.value, 1);
        if (out_thread.joinable())
            out_thread.join();
        throw;
    }
    auto begin = Clock::now(), disk_check = begin;
    std::exception_ptr failure;
    try
    {
        while (WaitForSingleObject(pi.hProcess, 100) == WAIT_TIMEOUT)
        {
            canceled(stop);
            if (Clock::now() - begin > timeout)
                throw std::runtime_error("Process timed out.");
            if (!output.empty() && Clock::now() - disk_check > std::chrono::seconds(2))
            {
                ensure_disk(output);
                disk_check = Clock::now();
            }
        }
        canceled(stop);
    }
    catch (...)
    {
        failure = std::current_exception();
    }
    DWORD exit = 1;
    GetExitCodeProcess(pi.hProcess, &exit);
    TerminateJobObject(job.value, 1); // Also closes inherited pipes in any surviving child.
    out_thread.join();
    err_thread.join();
    // Do not release update/download coordination until every process has exited.
    WaitForSingleObject(process.value, INFINITE);
    JOBOBJECT_BASIC_ACCOUNTING_INFORMATION accounting{};
    while (QueryInformationJobObject(job.value, JobObjectBasicAccountingInformation, &accounting, sizeof(accounting),
                                     nullptr) &&
           accounting.ActiveProcesses)
        Sleep(10);
    if (failure)
        std::rethrow_exception(failure);
    if (callback_error)
        std::rethrow_exception(callback_error);
    return static_cast<int>(exit);
}
std::string capture(const fs::path &executable, const std::vector<std::string> &arguments, std::atomic_bool &stop)
{
    std::string out;
    const auto exit = run_process(
        executable, arguments, executable.parent_path(), stop,
        [&](const std::string &line, bool) {
            if (out.size() < 1024 * 1024)
                out += line + "\n";
        },
        {}, std::chrono::seconds(20));
    if (exit)
    {
        std::ostringstream error;
        error << path_text(executable.filename()) << " exited with code 0x" << std::hex << std::uppercase
              << std::setw(8) << std::setfill('0') << static_cast<DWORD>(exit) << ".";
        if (static_cast<DWORD>(exit) == 0xC0000135)
            error << " A required DLL could not be found.";
        if (!trim(out).empty())
            error << " " << trim(out).substr(0, 4096);
        throw std::runtime_error(error.str());
    }
    return trim(out);
}
struct Internet
{
    HINTERNET value;
    explicit Internet(HINTERNET h) : value(h)
    {
        if (!h)
            throw std::runtime_error(error_text("HTTP request failed"));
    }
    ~Internet()
    {
        WinHttpCloseHandle(value);
    }
};
static void http(const std::string &url, std::atomic_bool &stop, const std::function<void(const char *, DWORD)> &sink,
                 const TransferProgress &progress, uint64_t max_size)
{
    canceled(stop);
    const auto wurl = wide(url);
    URL_COMPONENTS parts{};
    parts.dwStructSize = sizeof(parts);
    parts.dwHostNameLength = parts.dwUrlPathLength = parts.dwExtraInfoLength = static_cast<DWORD>(-1);
    if (!WinHttpCrackUrl(wurl.c_str(), 0, 0, &parts) || parts.nScheme != INTERNET_SCHEME_HTTPS)
        throw std::runtime_error("Tool downloads require HTTPS.");
    auto host = std::wstring(parts.lpszHostName, parts.dwHostNameLength);
    auto resource = std::wstring(parts.lpszUrlPath, parts.dwUrlPathLength) +
                    std::wstring(parts.lpszExtraInfo, parts.dwExtraInfoLength);
    Internet session(WinHttpOpen(L"OnlineVideoManager/0.2", WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY, WINHTTP_NO_PROXY_NAME,
                                 WINHTTP_NO_PROXY_BYPASS, 0));
    WinHttpSetTimeouts(session.value, 10000, 10000, 10000, 10000);
    Internet connection(WinHttpConnect(session.value, host.c_str(), parts.nPort, 0));
    Internet request(WinHttpOpenRequest(connection.value, L"GET", resource.c_str(), nullptr, WINHTTP_NO_REFERER,
                                        WINHTTP_DEFAULT_ACCEPT_TYPES, WINHTTP_FLAG_SECURE));
    DWORD redirect = WINHTTP_OPTION_REDIRECT_POLICY_DISALLOW_HTTPS_TO_HTTP;
    WinHttpSetOption(request.value, WINHTTP_OPTION_REDIRECT_POLICY, &redirect, sizeof(redirect));
    if (!WinHttpSendRequest(request.value, WINHTTP_NO_ADDITIONAL_HEADERS, 0, WINHTTP_NO_REQUEST_DATA, 0, 0, 0) ||
        !WinHttpReceiveResponse(request.value, nullptr))
        throw std::runtime_error(error_text("HTTP transfer failed"));
    DWORD status = 0, length = sizeof(status);
    WinHttpQueryHeaders(request.value, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER,
                        WINHTTP_HEADER_NAME_BY_INDEX, &status, &length, WINHTTP_NO_HEADER_INDEX);
    if (status != 200)
        throw std::runtime_error("Upstream returned HTTP " + std::to_string(status) +
                                 (status == 403 || status == 429 ? " (possibly rate limited)." : "."));
    DWORD total32 = 0;
    length = sizeof(total32);
    WinHttpQueryHeaders(request.value, WINHTTP_QUERY_CONTENT_LENGTH | WINHTTP_QUERY_FLAG_NUMBER,
                        WINHTTP_HEADER_NAME_BY_INDEX, &total32, &length, WINHTTP_NO_HEADER_INDEX);
    uint64_t done = 0;
    auto begin = Clock::now();
    auto reported = begin;
    std::array<char, 65536> buffer{};
    while (true)
    {
        canceled(stop);
        if (Clock::now() - begin > std::chrono::minutes(20))
            throw std::runtime_error("Tool download timed out.");
        DWORD read = 0;
        if (!WinHttpReadData(request.value, buffer.data(), static_cast<DWORD>(buffer.size()), &read))
            throw std::runtime_error(error_text("Cannot read HTTP response"));
        if (!read)
            break;
        done += read;
        if (done > max_size)
            throw std::runtime_error("Upstream file exceeds size limit.");
        sink(buffer.data(), read);
        if (progress && Clock::now() - reported > std::chrono::milliseconds(150))
        {
            progress(done, total32);
            reported = Clock::now();
        }
    }
    if (total32 && done != total32)
        throw std::runtime_error("Incomplete HTTP download.");
    if (progress)
        progress(done, total32);
}
std::string http_get(const std::string &url, std::atomic_bool &stop)
{
    std::string body;
    http(url, stop, [&](const char *b, DWORD n) { body.append(b, n); }, {}, 16 * 1024 * 1024);
    return body;
}
void http_download(const std::string &url, const fs::path &file, std::atomic_bool &stop,
                   const TransferProgress &progress)
{
    std::ofstream out(file, std::ios::binary | std::ios::trunc);
    if (!out)
        throw std::runtime_error("Cannot create tool archive.");
    http(
        url, stop,
        [&](const char *b, DWORD n) {
            out.write(b, n);
            if (!out)
                throw std::runtime_error("Cannot write tool archive (disk full or unavailable).");
        },
        progress, 2ull * 1024 * 1024 * 1024);
}
std::string sha256(const fs::path &file)
{
    BCRYPT_ALG_HANDLE algorithm{};
    BCRYPT_HASH_HANDLE hash{};
    if (BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) < 0)
        throw std::runtime_error("Cannot open SHA-256 provider.");
    struct Cleanup
    {
        BCRYPT_ALG_HANDLE &a;
        BCRYPT_HASH_HANDLE &h;
        ~Cleanup()
        {
            if (h)
                BCryptDestroyHash(h);
            BCryptCloseAlgorithmProvider(a, 0);
        }
    } cleanup{algorithm, hash};
    if (BCryptCreateHash(algorithm, &hash, nullptr, 0, nullptr, 0, 0) < 0)
        throw std::runtime_error("Cannot initialize SHA-256.");
    std::ifstream in(file, std::ios::binary);
    if (!in)
        throw std::runtime_error("Cannot read file for checksum.");
    std::array<char, 65536> buffer{};
    while (in)
    {
        in.read(buffer.data(), buffer.size());
        if (BCryptHashData(hash, reinterpret_cast<PUCHAR>(buffer.data()), static_cast<ULONG>(in.gcount()), 0) < 0)
            throw std::runtime_error("Cannot compute SHA-256.");
    }
    if (!in.eof())
        throw std::runtime_error("Checksum file read failed.");
    std::array<unsigned char, 32> digest{};
    if (BCryptFinishHash(hash, digest.data(), static_cast<ULONG>(digest.size()), 0) < 0)
        throw std::runtime_error("Cannot finalize SHA-256.");
    std::ostringstream hex;
    for (auto b : digest)
        hex << std::hex << std::setw(2) << std::setfill('0') << static_cast<int>(b);
    return hex.str();
}
std::vector<fs::path> extract_zip(const fs::path &archive, const fs::path &out, const std::string &tool,
                                  std::atomic_bool &stop)
{
    // FILE* keeps non-ASCII Windows paths working without miniz's narrow filename API.
    FILE *input = nullptr;
    if (_wfopen_s(&input, archive.c_str(), L"rb") || !input)
        throw std::runtime_error("Cannot open ZIP archive.");
    mz_zip_archive zip{};
    struct Cleanup
    {
        mz_zip_archive &z;
        FILE *f;
        ~Cleanup()
        {
            mz_zip_reader_end(&z);
            fclose(f);
        }
    } cleanup{zip, input};
    if (!mz_zip_reader_init_cfile(&zip, input, 0, 0))
        throw std::runtime_error("Invalid ZIP archive.");
    fs::create_directories(out);
    std::vector<fs::path> files;
    uint64_t expanded = 0;
    for (mz_uint i = 0; i < mz_zip_reader_get_num_files(&zip); ++i)
    {
        canceled(stop);
        mz_zip_archive_file_stat info{};
        if (!mz_zip_reader_file_stat(&zip, i, &info))
            throw std::runtime_error("Invalid ZIP entry.");
        std::string entry = info.m_filename;
        std::replace(entry.begin(), entry.end(), '\\', '/');
        if (info.m_is_directory)
            continue;
        auto relative = path(entry);
        auto name = relative.filename();
        if ((tool == "deno" && name != L"deno.exe") ||
            (tool == "ffmpeg" && relative.parent_path().filename() != L"bin"))
            continue;
        if (name.empty() || name == L"." || name == L".." || name.wstring().find(L':') != std::wstring::npos)
            throw std::runtime_error("Unsafe ZIP filename.");
        auto destination = out / name;
        if (fs::exists(destination))
            throw std::runtime_error("Duplicate ZIP filename.");
        expanded += info.m_uncomp_size;
        if (expanded > 2ull * 1024 * 1024 * 1024)
            throw std::runtime_error("Tool archive expands beyond size limit.");
        std::ofstream output(destination, std::ios::binary);
        auto write = [](void *opaque, mz_uint64, const void *data, size_t n) -> size_t {
            auto &stream = *static_cast<std::ofstream *>(opaque);
            stream.write(static_cast<const char *>(data), static_cast<std::streamsize>(n));
            return stream ? n : 0;
        };
        if (!output || !mz_zip_reader_extract_to_callback(&zip, i, write, &output, 0))
            throw std::runtime_error("ZIP extraction or CRC verification failed.");
        files.push_back(destination);
    }
    return files;
}
} // namespace ovm
