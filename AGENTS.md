# Online Video Manager

C# 14 / .NET 10, WinUI 3, dark theme, CommunityToolkit.Mvvm. Windows x64.

- `native/`: C++20 engine compiled by MSVC/CMake to `ovm_core.dll`, exporting the C ABI in `include/ovm.h`. It owns link qualification, settings, persistence, queue scheduling, media policy, process trees, progress, disk checks, and verified tool installation.
- `Contracts`: C# wire DTOs and enums only. No engine implementation or business defaults.
- `Interop`: marshalling and lifetime management only; every application operation goes through this C ABI.
- `ViewModels`: UI-independent MVVM, native snapshot presentation, and platform service interfaces. No queue scheduling, tool execution, networking, or persistence.
- `App`: Windows platform services and views. Keep business logic out of code-behind.
- Package with `pwsh scripts/Package.ps1`; ship MSIX, including the native engine.
- Use centrally pinned NuGet versions, partial observable properties, and source-generated JSON.
- Preserve checksum verification, cancellation of process trees, and update/download coordination.
- Clipboard capture collects only; it must never start downloads.
- Run `pwsh scripts/Build.ps1` (includes native/C-client tests), then `dotnet test --project tests/OnlineVideoManager.Tests`. Use `Build-Native.ps1` for native-only iteration.
- Never commit signing keys, downloaded tools, or generated build output.
