using OnlineVideoManager.Core.Downloads;
using OnlineVideoManager.Core.Settings;
using OnlineVideoManager.Core.Tools;

namespace OnlineVideoManager.Interop;

public sealed class NativeDownloadRunner : IDownloadRunner
{
    public async Task<int> RunAsync(DownloadRequest request, Action<JobUpdate> onUpdate, CancellationToken ct) =>
        (await NativeCore.CallAsync(new("download", Download: request),
            e => { if (e.Download is { } update) onUpdate(update); }, ct).ConfigureAwait(false)).ExitCode;
}

public sealed class NativeToolManager : IToolManager
{
    public async Task<string?> GetLocalVersionAsync(string directory, ToolId id, CancellationToken ct = default) =>
        (await NativeCore.CallAsync(new("version", directory, id), ct: ct).ConfigureAwait(false)).Version;
    public async Task<ToolRelease> GetLatestAsync(ToolId id, UpdateChannel channel, CancellationToken ct = default) =>
        (await NativeCore.CallAsync(new("latest", Tool: id, Channel: channel), ct: ct).ConfigureAwait(false)).Release!;
    public async Task InstallAsync(string directory, ToolId id, ToolRelease release, IProgress<ToolInstallProgress>? progress, CancellationToken ct = default) =>
        await NativeCore.CallAsync(new("install", directory, id, Release: release),
            e => { if (e.Install is { } update) progress?.Report(update); }, ct).ConfigureAwait(false);
    public void Cleanup(string directory) => NativeCore.Call(new("cleanup", directory));
}
