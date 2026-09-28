using System.Text.Json.Serialization;
namespace OnlineVideoManager.Core.Downloads;

/// <summary>Something yt-dlp reported about a running download.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(JobUpdate.Title), "Title")]
[JsonDerivedType(typeof(JobUpdate.Streams), "Streams")]
[JsonDerivedType(typeof(JobUpdate.Destination), "Destination")]
[JsonDerivedType(typeof(JobUpdate.Progress), "Progress")]
[JsonDerivedType(typeof(JobUpdate.PlaylistItem), "PlaylistItem")]
[JsonDerivedType(typeof(JobUpdate.Stage), "Stage")]
[JsonDerivedType(typeof(JobUpdate.FileSaved), "FileSaved")]
[JsonDerivedType(typeof(JobUpdate.Log), "Log")]
public abstract record JobUpdate
{
    public sealed record Title(string Value) : JobUpdate;

    /// <summary>Number of streams that will be downloaded and merged (video + audio = 2).</summary>
    public sealed record Streams(int Count) : JobUpdate;

    /// <summary>A new stream began downloading.</summary>
    public sealed record Destination : JobUpdate;

    public sealed record Progress(double? Downloaded, double? Total, double? Speed, double? Eta) : JobUpdate;

    public sealed record PlaylistItem(int Index, int Count) : JobUpdate;

    /// <summary>A post-processing step started (e.g. "Merger", "ExtractAudio").</summary>
    public sealed record Stage(string Name) : JobUpdate;

    public sealed record FileSaved(string Path) : JobUpdate;

    public sealed record Log(string Line, bool IsError) : JobUpdate;
}
