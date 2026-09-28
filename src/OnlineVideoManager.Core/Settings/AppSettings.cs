namespace OnlineVideoManager.Core.Settings;

/// <summary>User settings, persisted as JSON by <see cref="SettingsStore"/>.
/// Missing properties in the file fall back to the defaults below.</summary>
public sealed class AppSettings
{
    public static readonly int[] MaxHeightChoices = [0, 4320, 2160, 1440, 1080, 720, 480, 360];

    // Downloads
    public string OutputDirectory { get; set; } = AppPaths.DefaultOutputDirectory;
    public string OutputTemplate { get; set; } = "%(title)s [%(id)s].%(ext)s";
    public DownloadMode DefaultMode { get; set; } = DownloadMode.Video;
    public VideoContainer Container { get; set; } = VideoContainer.Mkv;
    public VideoCodec VideoCodec { get; set; } = VideoCodec.Best;

    /// <summary>Highest video height to prefer; 0 means no limit.</summary>
    public int MaxHeight { get; set; }

    public AudioFormat AudioFormat { get; set; } = AudioFormat.Best;
    public int Concurrency { get; set; } = 2;
    public bool StartQueueAutomatically { get; set; }
    public bool EmbedMetadata { get; set; } = true;
    public bool EmbedThumbnail { get; set; } = true;
    public bool EmbedSubtitles { get; set; }
    public string SubtitleLanguages { get; set; } = "en.*";

    /// <summary>For a link to a video inside a playlist, download only that video.
    /// Links to a playlist itself still download the whole playlist.</summary>
    public bool SingleVideoFromPlaylistLinks { get; set; } = true;

    public bool UseDownloadArchive { get; set; }
    public string CookiesFromBrowser { get; set; } = "";
    public string RateLimit { get; set; } = "";
    public string ExtraArguments { get; set; } = "";

    // Clipboard
    public bool WatchClipboard { get; set; } = true;
    public bool AutoQueueClipboardLinks { get; set; }
    public LinkFilterMode LinkFilter { get; set; } = LinkFilterMode.KnownSites;
    public List<string> ExtraSites { get; set; } = [];

    // Tools
    /// <summary>Folder holding yt-dlp, deno and ffmpeg. Null or empty means automatic.</summary>
    public string? ToolsDirectory { get; set; }

    public UpdateChannel YtDlpChannel { get; set; } = UpdateChannel.Stable;
    public bool CheckForToolUpdatesOnStartup { get; set; } = true;
    public bool InstallToolUpdatesAutomatically { get; set; } = true;

    public string ResolvedToolsDirectory => AppPaths.ResolveToolsDirectory(ToolsDirectory);

    /// <summary>Clamp values a hand-edited file could have put out of range.</summary>
    public void Normalize()
    {
        AutoQueueClipboardLinks = false;
        if (!Enum.IsDefined(VideoCodec)) VideoCodec = VideoCodec.Best;
        if (!Enum.IsDefined(Container)) Container = VideoContainer.Mkv;
        if (!Enum.IsDefined(AudioFormat)) AudioFormat = AudioFormat.Best;
        Concurrency = Math.Clamp(Concurrency, 1, 8);
        if (MaxHeight < 0)
        {
            MaxHeight = 0;
        }

        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            OutputDirectory = AppPaths.DefaultOutputDirectory;
        }

        if (string.IsNullOrWhiteSpace(OutputTemplate))
        {
            OutputTemplate = "%(title)s [%(id)s].%(ext)s";
        }

        OutputTemplate = OutputTemplate.Trim();
        SubtitleLanguages = SubtitleLanguages?.Trim() ?? "";
        CookiesFromBrowser = CookiesFromBrowser?.Trim() ?? "";
        RateLimit = RateLimit?.Trim() ?? "";
        ExtraArguments ??= "";
        ExtraSites = (ExtraSites ?? [])
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
