namespace OnlineVideoManager.Core.Downloads;

public sealed record DownloadRequest(string YtDlpPath, string ToolsDirectory, IReadOnlyList<string> Arguments,
    string? OutputDirectory = null, long MinimumFreeBytes = DiskSpace.DefaultReserve);

public interface IDownloadRunner
{
    /// <summary>Run one download to completion and return yt-dlp's exit code.
    /// <paramref name="onUpdate"/> is called from background threads.
    /// Cancelling kills yt-dlp and everything it started, then throws <see cref="OperationCanceledException"/>.</summary>
    Task<int> RunAsync(DownloadRequest request, Action<JobUpdate> onUpdate, CancellationToken ct);
}
