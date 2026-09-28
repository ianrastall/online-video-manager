#include <iostream>
#include <filesystem>
#include <fstream>
#include <string>
#include <vector>
#include <windows.h>

// Test fixture executable. No media or network operations.
int main(int argc, char **argv)
{
    if (argc > 1 && std::string(argv[1]) == "--check-paths")
    {
        std::vector<wchar_t> own(32768);
        GetModuleFileNameW(nullptr, own.data(), static_cast<DWORD>(own.size()));
        const std::filesystem::path executable(own.data());
        if (!std::ifstream(executable, std::ios::binary) ||
            !std::ifstream(executable.parent_path() / L"sidecar.txt") || !std::ifstream("sidecar.txt"))
        {
            std::cerr << "Cannot reopen executable or adjacent files from the child process.\n";
            return 4;
        }
        std::cout << "Child paths are accessible\n";
        return 0;
    }
    if (argc > 1 && std::string(argv[1]) == "--child")
    {
        Sleep(60000);
        return 0;
    }
    if (argc > 1 && std::string(argv[1]) == "--version")
    {
        std::cout << "1.0.0\n";
        return 0;
    }
    if (argc > 1 && std::string(argv[argc - 1]) == "-version")
    {
        std::cout << "ffmpeg version 1.0.0\n";
        return 0;
    }
    const std::string argument = argc > 1 ? argv[argc - 1] : "";
    if (argument == "--tree" || argument.find("slow") != std::string::npos)
    {
        std::vector<wchar_t> own(32768);
        GetModuleFileNameW(nullptr, own.data(), static_cast<DWORD>(own.size()));
        std::wstring command = L"\"" + std::wstring(own.data()) + L"\" --child";
        STARTUPINFOW startup{};
        startup.cb = sizeof(startup);
        PROCESS_INFORMATION child{};
        if (!CreateProcessW(own.data(), command.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, nullptr,
                            &startup, &child))
            return 2;
        std::cout << child.dwProcessId << std::endl;
        CloseHandle(child.hThread);
        CloseHandle(child.hProcess);
        Sleep(60000);
    }
    std::cout << "@@T Fixture title\n@@P 50|100|NA|25|2\n";
    if (argument.find("fail") != std::string::npos)
    {
        std::cerr << "ERROR: fixture failure\n";
        return 3;
    }
    return 0;
}
