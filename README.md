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

Install the .NET 10 SDK, Windows SDK, and Visual Studio C++ build tools (Desktop development with C++, required by NativeAOT). Visual Studio with WinUI tooling is optional for editing.

```powershell
dotnet test --project tests/OnlineVideoManager.Tests
./scripts/Build.ps1
./scripts/Smoke-Native.ps1
./scripts/Smoke-App.ps1
./scripts/Package.ps1 -SkipBuild -DevelopmentCertificate
```

`Build.ps1` publishes the native DLL first and the WinUI app second. For local development, run `artifacts/app/OnlineVideoManager.exe` or build/run the App project after the native build. The app's publish payload is self-contained and is wrapped by `Package.ps1` in MSIX; `WindowsPackageType=None` describes the payload build, not the delivered installer.

To test real downloads of upstream tools and a one-second local media fixture, run `./scripts/Smoke-Native.ps1 -Live`. It installs tools in `artifacts/smoke/tools`, exercises native process execution, cancellation, errors, disk refusal, callbacks, and real MP4/FLAC output without relying on a third-party video account. Regular tests cover queue behavior, clipboard persistence, parsing, settings, codec selection, checksum rejection, and tool installation. `Smoke-App.ps1` launches the published WinUI app with isolated data and loads all three pages. Interactive visual inspection and clean-machine MSIX installation still require a manual Windows acceptance pass.

For release signing:

```powershell
./scripts/Package.ps1 -CertificateThumbprint YOUR_CERTIFICATE_THUMBPRINT -Publisher 'CN=Your Publisher'
```

The publisher must exactly match the signing certificate subject. CI builds and tests on Windows and uploads an **unsigned** MSIX artifact.

## Architecture

| Project | Responsibility |
| --- | --- |
| `OnlineVideoManager.App` | WinUI views, Win32 clipboard notifications, pickers, shell integration, composition |
| `OnlineVideoManager.ViewModels` | UI-independent MVVM, queue scheduling and update coordination |
| `OnlineVideoManager.Interop` | Managed adapters calling the native C ABI; no direct process/tool implementation |
| `OnlineVideoManager.Native` | NativeAOT `ovm_core.dll`; download process execution, progress parsing, disk monitoring, tool discovery and installation |
| `OnlineVideoManager.Core` | Shared contracts and UI-free engine source; link filtering, argument construction and JSON persistence also used by managed presentation services |

The core is implemented in C# and compiled to native machine code with NativeAOT. Its external interface is a C ABI, not a requirement for a C-language implementation. [`include/ovm.h`](include/ovm.h) documents ownership, threading and lifetime. ABI 1 uses UTF-8 JSON requests/responses and events, explicit cancellation handles, and explicit result deallocation. Errors never intentionally cross the ABI as exceptions. Keep the DLL loaded until process exit.

Data is stored under `%LOCALAPPDATA%/OnlineVideoManager` (Windows may redirect this into package-local storage for MSIX), with `OVM_HOME` as a test/development override. Default output is `Videos/OVM`. The application writes tools to per-user storage, never into the read-only MSIX install directory.

## Provenance and upstream sources

OVM carries forward code from Ian Rastall's `universal-video-downloader` project, itself the successor to `yt-dlp-manager`. The original MIT license is retained. Old working directories and Git history are not modified by the migration.

- [yt-dlp options and dependencies](https://github.com/yt-dlp/yt-dlp)
- [yt-dlp EJS/runtime requirements](https://github.com/yt-dlp/yt-dlp/wiki/EJS)
- [Deno releases](https://github.com/denoland/deno/releases)
- [BtbN FFmpeg builds](https://github.com/BtbN/FFmpeg-Builds)
- [.NET NativeAOT libraries](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/libraries)
- [MSIX packaging](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-manual-conversion)

External utilities retain their respective licenses; FFmpeg downloads use BtbN's GPL shared build. They are acquired at runtime and not bundled into OVM's repository or installer.
