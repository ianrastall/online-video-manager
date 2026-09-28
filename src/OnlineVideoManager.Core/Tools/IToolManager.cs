using OnlineVideoManager.Core.Settings;

namespace OnlineVideoManager.Core.Tools;

public interface IToolManager
{
    /// <summary>Installed version; null if the tool is missing, "unknown" if present but unreadable.</summary>
    Task<string?> GetLocalVersionAsync(string directory, ToolId id, CancellationToken ct = default);

    Task<ToolRelease> GetLatestAsync(ToolId id, UpdateChannel channel, CancellationToken ct = default);

    Task InstallAsync(string directory, ToolId id, ToolRelease release, IProgress<ToolInstallProgress>? progress, CancellationToken ct = default);

    /// <summary>Remove leftovers from earlier updates (staging folder, renamed old binaries).</summary>
    void Cleanup(string directory);
}
