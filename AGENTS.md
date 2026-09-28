# Online Video Manager

C# 14 / .NET 10, WinUI 3, dark theme, CommunityToolkit.Mvvm. Windows x64.

- `Core`: UI-free engine and contracts; do not add WinUI dependencies.
- `Native`: NativeAOT DLL exporting the C ABI documented in `include/ovm.h`.
- `Interop`: managed client of that ABI; the app must use these adapters for downloads and tools.
- `ViewModels`: UI-independent MVVM and platform service interfaces.
- `App`: Windows platform services and views. Keep business logic out of code-behind.
- Package with `pwsh scripts/Package.ps1`; ship MSIX, including the native engine.
- Use centrally pinned NuGet versions, partial observable properties, and source-generated JSON.
- Preserve checksum verification, cancellation of process trees, and update/download coordination.
- Clipboard capture collects only; it must never start downloads.
- Run `dotnet test --project tests/OnlineVideoManager.Tests` and `pwsh scripts/Build.ps1`.
- Never commit signing keys, downloaded tools, or generated build output.
