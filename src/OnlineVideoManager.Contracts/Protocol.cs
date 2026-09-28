using System.Text.Json.Serialization;

namespace OnlineVideoManager.Contracts;

public enum DownloadMode { Video, Audio }
public enum VideoContainer { Mkv, Mp4, Auto }
public enum VideoCodec { Best, H264, Hevc, Av1, Vp9 }
public enum AudioFormat { Best, Opus, M4a, Mp3, Flac }
public enum UpdateChannel { Stable, Nightly }
public enum LinkFilterMode { KnownSites, AnyLink }
public enum DownloadStatus { Queued, Running, Completed, Failed, Canceled }

// Wire DTOs only. Defaults, validation, policy, persistence and execution live in C++.
public sealed class AppSettings
{
    public string OutputDirectory { get; set; } = "";
    public string OutputTemplate { get; set; } = "";
    public DownloadMode DefaultMode { get; set; }
    public VideoContainer Container { get; set; }
    public VideoCodec VideoCodec { get; set; }
    public int MaxHeight { get; set; }
    public AudioFormat AudioFormat { get; set; }
    public int Concurrency { get; set; }
    public bool StartQueueAutomatically { get; set; }
    public bool EmbedMetadata { get; set; }
    public bool EmbedThumbnail { get; set; }
    public bool EmbedSubtitles { get; set; }
    public string SubtitleLanguages { get; set; } = "";
    public bool SingleVideoFromPlaylistLinks { get; set; }
    public bool UseDownloadArchive { get; set; }
    public string CookiesFromBrowser { get; set; } = "";
    public string RateLimit { get; set; } = "";
    public string ExtraArguments { get; set; } = "";
    public bool WatchClipboard { get; set; }
    public LinkFilterMode LinkFilter { get; set; }
    public List<string> ExtraSites { get; set; } = [];
    public string ToolsDirectory { get; set; } = "";
    public UpdateChannel YtDlpChannel { get; set; }
    public bool CheckForToolUpdatesOnStartup { get; set; }
    public bool InstallToolUpdatesAutomatically { get; set; }
}
public sealed record EngineRequest(string Operation, string? Text = null, string? Html = null,
    string? Path = null, DownloadMode? Mode = null, string? Id = null, string[]? Urls = null,
    AppSettings? Settings = null, string? Tool = null, string? Kind = null);
public sealed class EngineReply
{
    public ulong Handle { get; set; }
    public string? Error { get; set; }
    public EngineSnapshot? State { get; set; }
    public int Added { get; set; }
    public int Skipped { get; set; }
    public int Count { get; set; }
    public string? Path { get; set; }
    public string[] Links { get; set; } = [];
}
public sealed class EngineSnapshot
{
    public ulong Revision { get; set; }
    public AppSettings Settings { get; set; } = new();
    public string DataDirectory { get; set; } = "";
    public string ToolsDirectory { get; set; } = "";
    public string[] KnownSites { get; set; } = [];
    public DownloadSnapshot[] Items { get; set; } = [];
    public string[] Inbox { get; set; } = [];
    public bool Running { get; set; }
    public bool ToolsBusy { get; set; }
    public ToolSnapshot[] Tools { get; set; } = [];
    public string ToolStatus { get; set; } = "";
    public string[] ToolLogs { get; set; } = [];
    public ulong NoticeId { get; set; }
    public string Notice { get; set; } = "";
}
public sealed class DownloadSnapshot
{
    public string Id { get; set; } = "";
    public string Url { get; set; } = "";
    public DownloadMode Mode { get; set; }
    public DownloadStatus Status { get; set; }
    public string Title { get; set; } = "";
    public string Error { get; set; } = "";
    public string FilePath { get; set; } = "";
    public double Progress { get; set; }
    public bool Indeterminate { get; set; }
    public string Stage { get; set; } = "";
    public double? Downloaded { get; set; }
    public double? Total { get; set; }
    public double? Speed { get; set; }
    public double? Eta { get; set; }
    public int Streams { get; set; }
    public int Stream { get; set; }
    public int PlaylistIndex { get; set; }
    public int PlaylistCount { get; set; }
    public string[] Logs { get; set; } = [];
}
public sealed class ToolSnapshot
{
    public string Id { get; set; } = "";
    public string Installed { get; set; } = "";
    public string Latest { get; set; } = "";
    public string Error { get; set; } = "";
    public string Activity { get; set; } = "";
    public double Progress { get; set; }
    public bool Working { get; set; }
}
public interface IEngine : IDisposable
{
    EngineReply Execute(EngineRequest request);
    Task<EngineReply> ExecuteAsync(EngineRequest request);
}
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(EngineRequest))]
[JsonSerializable(typeof(EngineReply))]
public sealed partial class ProtocolJson : JsonSerializerContext;
