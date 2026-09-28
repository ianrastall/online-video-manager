namespace OnlineVideoManager.Core.Tools;

/// <summary>An upstream release asset for one tool.</summary>
public sealed record ToolRelease(string Version, string AssetName, string AssetUrl, string? ChecksumUrl, long? Size);

public sealed record ToolInstallProgress(string Stage, long BytesDone = 0, long? BytesTotal = null)
{
    public double? Fraction => BytesTotal is > 0 ? Math.Clamp((double)BytesDone / BytesTotal.Value, 0, 1) : null;
}
