# Online Video Manager (OVM)

A packaged Windows desktop application for collecting video links and downloading the best available video or audio. C# 14, .NET 10, WinUI 3, dark theme, and CommunityToolkit.Mvvm. The Windows toolkit package is `CommunityToolkit.WinUI.Extensions`.

## Use

1. Copy links while OVM is open. Supported-site links collect in a persistent clipboard inbox, including while OVM is in the background. Capture never starts a download. You can turn monitoring off or extend the site filter.
2. Select collected links and add them to the queue, paste URLs directly, import a text file, or drop links/text files onto the window.
3. Choose an output folder and defaults in Settings, then press **Start**. Fresh and restored queues start paused. Automatic start for manually added links is optional.

The queue shows stream/playlist progress, speed, time remaining, post-processing, and per-item logs. Cancel stops the process tree. Retry resumes supported partial downloads. Unfinished queue entries, errors, settings, and collected links survive restart.

Video defaults to the best source video plus best source audio, without a resolution limit, merged into MKV. MP4 and automatic containers are available. Codec choices include H.264, HEVC, AV1, and VP9: a specific choice selects the best matching source and fails if unavailable, rather than silently selecting another codec. Audio-only options include original, Opus, AAC/M4A, MP3, and FLAC. Conversion to FLAC cannot recover information already lost in the source. MP4 is a container, not a codec; device playback support still depends on the source codecs. Individual source-format inspection/picking is not implemented yet.

OVM installs and checks for updates to yt-dlp, Deno, and FFmpeg/ffprobe at startup and every 24 hours while open; manual checks and per-tool actions are on the Tools page. Checks and automatic installation can be disabled separately. Downloads are checked against upstream SHA-256 checksums. Tool replacement waits for active downloads. Missing ffprobe triggers repair of the FFmpeg installation. The official standalone yt-dlp binary includes its EJS challenge scripts; no separate Python installation is needed.

Disk checks run before a download and every two seconds during downloading/post-processing. OVM stops work when less than 2 GiB remains on the output volume, and checks space before tool installation. This is a reserve, not an exact prediction of a video's final size; concurrent external disk activity can still exhaust storage between checks.

## Install

Windows 10 version 2004 (build 19041) or later, Windows 11, x64. The MSIX includes .NET and Windows App SDK runtimes plus the native OVM engine. Media utilities are downloaded on first launch.

The locally built installer is in `artifacts/installer`. A development build is signed with a self-signed certificate. Trust is **not** installed by the build. To install that development build on your own machine, run PowerShell as administrator in that folder:

```powershell
./Install.ps1 -TrustDevelopmentCertificate
```

This explicitly imports the adjacent development certificate into `LocalMachine/TrustedPeople`, then installs the MSIX. For an already trusted signing certificate, run `./Install.ps1` without elevation. Launch **Online Video Manager** from Start. Uninstall through Windows Settings. Public distribution requires a trusted signing certificate or Microsoft Store signing; do not distribute the private development key.

## Build and verify

Install the .NET 10 SDK, Windows SDK, and Visual Studio 2022 or 2026 with Desktop development with C++ and CMake tools. The engine is ordinary C++20 compiled by MSVC. Visual Studio with WinUI tooling is optional for editing.

```powershell
./scripts/Build.ps1
dotnet test --project tests/OnlineVideoManager.Tests
./scripts/Smoke-Native.ps1
./scripts/Smoke-App.ps1
./scripts/Package.ps1 -SkipBuild -DevelopmentCertificate
```

`Build.ps1` builds the C++ DLL, runs native tests and a plain C ABI client, then publishes the WinUI app. For local development, run `artifacts/app-v2/OnlineVideoManager.exe`. `Build-Native.ps1` builds and tests only the engine. CMake fetches nlohmann/json and miniz from pinned releases with SHA-256 verification; their licenses accompany the published app. The self-contained payload is wrapped by `Package.ps1` in MSIX.

To test upstream downloads and a one-second local media fixture, run `./scripts/Smoke-Native.ps1 -Live`. The C++ engine installs tools in `artifacts/cpp-smoke-tools`, verifies their checksums, and produces MP4/FLAC output through its own queue. Offline native tests cover process-tree cancellation, update/download coordination, failed-install rollback, ZIP extraction, disk refusal, parsing, and persistence. Managed integration tests exercise the DLL through P/Invoke and the MVVM projections. `Smoke-App.ps1` launches the published WinUI app with isolated data and loads all three pages. Clean-machine MSIX installation still requires a Windows acceptance pass.

For release signing:

```powershell
./scripts/Package.ps1 -CertificateThumbprint YOUR_CERTIFICATE_THUMBPRINT -Publisher 'CN=Your Publisher'
```

The publisher must exactly match the signing certificate subject. CI builds and tests on Windows and uploads an **unsigned** MSIX artifact.

## Architecture

| Project | Responsibility |
| --- | --- |
| `OnlineVideoManager.App` | WinUI views, Win32 clipboard notifications, pickers, shell integration, composition |
| `OnlineVideoManager.ViewModels` | UI-independent MVVM; sends commands and displays native snapshots |
| `OnlineVideoManager.Contracts` | Wire DTOs, enums and source-generated JSON |
| `OnlineVideoManager.Interop` | P/Invoke, UTF-8 JSON marshalling, and SafeHandle lifetime |
| `native/` | C++20 `ovm_core.dll`; all application state and business logic |

The engine is implemented in C++, with no CLR dependency. It owns URL extraction and qualification, codec/quality policy, command construction, queue scheduling, settings and inbox persistence, progress parsing, disk checks, Windows Job Objects, HTTPS transfers, SHA-256 verification, archive extraction, and transactional tool replacement. C# handles WinUI/MVVM presentation and Windows UI services.

[`include/ovm.h`](include/ovm.h) and [the ABI reference](docs/abi.md) document ABI 2: five C exports, opaque integer handles, UTF-8 JSON commands/snapshots, and explicit result deallocation. The plain C test client runs the engine without .NET. This replaces the former C# NativeAOT implementation; existing settings, queue and inbox JSON are read by the native engine. ABI 1 clients must migrate.

Data is stored under `%LOCALAPPDATA%/OnlineVideoManager` (Windows may redirect this into package-local storage for MSIX), with `OVM_HOME` as a test/development override. Default output is `Videos/OVM`. The application writes tools to per-user storage, never into the read-only MSIX install directory.

## Provenance and upstream sources

OVM carries forward code from Ian Rastall's `universal-video-downloader` project, itself the successor to `yt-dlp-manager`. The original MIT license is retained. Old working directories and Git history are not modified by the migration.

- [yt-dlp options and dependencies](https://github.com/yt-dlp/yt-dlp)
- [yt-dlp EJS/runtime requirements](https://github.com/yt-dlp/yt-dlp/wiki/EJS)
- [Deno releases](https://github.com/denoland/deno/releases)
- [BtbN FFmpeg builds](https://github.com/BtbN/FFmpeg-Builds)
- [Windows Job Objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects)
- [nlohmann/json](https://github.com/nlohmann/json)
- [miniz](https://github.com/richgel999/miniz)
- [MSIX packaging](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-manual-conversion)

External utilities retain their respective licenses; FFmpeg downloads use BtbN's GPL shared build. They are acquired at runtime and not bundled into OVM's repository or installer.
