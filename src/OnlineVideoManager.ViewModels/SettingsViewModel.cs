using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OnlineVideoManager.Contracts;


using OnlineVideoManager.ViewModels.Services;

namespace OnlineVideoManager.ViewModels;

/// <summary>Two-way view of <see cref="AppSettings"/>. Every change is saved immediately.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private static readonly (string Label, string Value)[] Browsers =
    [
        ("None", ""), ("Firefox", "firefox"), ("Chrome", "chrome"), ("Edge", "edge"), ("Brave", "brave"),
        ("Opera", "opera"), ("Vivaldi", "vivaldi"), ("Chromium", "chromium"), ("Whale", "whale"),
    ];

    private readonly SettingsService _settings;
    private readonly IPickerService _pickers;
    private readonly NotificationViewModel _notifications;

    public SettingsViewModel(SettingsService settings, IPickerService pickers, NotificationViewModel notifications)
    {
        _settings = settings;
        _pickers = pickers;
        _notifications = notifications;
        _settings.Changed += (_, _) => OnPropertyChanged(nameof(WatchClipboard));
    }

    private static readonly int[] HeightChoices = [0, 4320, 2160, 1440, 1080, 720, 480, 360];
    private AppSettings S => _settings.Current;

    // ----- option lists for combo boxes -------------------------------------------------

    // Plain arrays: they marshal to XAML ItemsSource as bindable vectors.
    public string[] ModeOptions { get; } = ["Video", "Audio only"];

    public string[] ContainerOptions { get; } =
        ["MKV (keeps original streams)", "MP4", "Automatic"];

    public string[] MaxHeightOptions { get; } = HeightChoices
        .Select(h => h == 0 ? "Best available" : string.Create(CultureInfo.InvariantCulture, $"Up to {h}p"))
        .ToArray();

    public string[] AudioFormatOptions { get; } =
        ["Original (no re-encode)", "Opus", "M4A (AAC)", "MP3", "FLAC"];

    public string[] ChannelOptions { get; } = ["Stable", "Nightly"];
    public string[] VideoCodecOptions { get; } = ["Best available (any codec)", "H.264 / AVC", "H.265 / HEVC", "AV1", "VP9"];
    public int VideoCodecIndex
    {
        get => (int)S.VideoCodec;
        set => SetIndex(value, VideoCodecOptions.Length, (int)S.VideoCodec, v => S.VideoCodec = (VideoCodec)v);
    }

    public string[] BrowserOptions { get; } = Browsers.Select(b => b.Label).ToArray();

    public string KnownSitesText => string.Join(", ", _settings.KnownSites);

    // ----- downloads ------------------------------------------------------------------------

    public string OutputDirectory
    {
        get => S.OutputDirectory;
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && SetProperty(S.OutputDirectory, value.Trim(), S, static (s, v) => s.OutputDirectory = v))
            {
                Save();
            }
        }
    }

    public string OutputTemplate
    {
        get => S.OutputTemplate;
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && SetProperty(S.OutputTemplate, value.Trim(), S, static (s, v) => s.OutputTemplate = v))
            {
                Save();
            }
        }
    }

    public int DefaultModeIndex
    {
        get => (int)S.DefaultMode;
        set => SetIndex(value, ModeOptions.Length, (int)S.DefaultMode, v => S.DefaultMode = (DownloadMode)v);
    }

    public int ContainerIndex
    {
        get => (int)S.Container;
        set => SetIndex(value, ContainerOptions.Length, (int)S.Container, v => S.Container = (VideoContainer)v);
    }

    public int MaxHeightIndex
    {
        get => Math.Max(0, Array.IndexOf(HeightChoices, S.MaxHeight));
        set => SetIndex(value, MaxHeightOptions.Length, MaxHeightIndex, v => S.MaxHeight = HeightChoices[v]);
    }

    public int AudioFormatIndex
    {
        get => (int)S.AudioFormat;
        set => SetIndex(value, AudioFormatOptions.Length, (int)S.AudioFormat, v => S.AudioFormat = (AudioFormat)v);
    }

    public double Concurrency
    {
        get => S.Concurrency;
        set
        {
            if (double.IsNaN(value))
            {
                return;
            }

            var v = Math.Clamp((int)Math.Round(value), 1, 8);
            if (SetProperty(S.Concurrency, v, S, static (s, x) => s.Concurrency = x))
            {
                Save();
            }
        }
    }

    public bool StartQueueAutomatically
    {
        get => S.StartQueueAutomatically;
        set => SetFlag(S.StartQueueAutomatically, value, v => S.StartQueueAutomatically = v);
    }

    public bool EmbedMetadata
    {
        get => S.EmbedMetadata;
        set => SetFlag(S.EmbedMetadata, value, v => S.EmbedMetadata = v);
    }

    public bool EmbedThumbnail
    {
        get => S.EmbedThumbnail;
        set => SetFlag(S.EmbedThumbnail, value, v => S.EmbedThumbnail = v);
    }

    public bool EmbedSubtitles
    {
        get => S.EmbedSubtitles;
        set => SetFlag(S.EmbedSubtitles, value, v => S.EmbedSubtitles = v);
    }

    public string SubtitleLanguages
    {
        get => S.SubtitleLanguages;
        set
        {
            if (SetProperty(S.SubtitleLanguages, (value ?? "").Trim(), S, static (s, v) => s.SubtitleLanguages = v))
            {
                Save();
            }
        }
    }

    public bool SingleVideoFromPlaylistLinks
    {
        get => S.SingleVideoFromPlaylistLinks;
        set => SetFlag(S.SingleVideoFromPlaylistLinks, value, v => S.SingleVideoFromPlaylistLinks = v);
    }

    public bool UseDownloadArchive
    {
        get => S.UseDownloadArchive;
        set => SetFlag(S.UseDownloadArchive, value, v => S.UseDownloadArchive = v);
    }

    public int CookiesBrowserIndex
    {
        get => Math.Max(0, Array.FindIndex(Browsers, b => b.Value.Equals(S.CookiesFromBrowser, StringComparison.OrdinalIgnoreCase)));
        set => SetIndex(value, Browsers.Length, CookiesBrowserIndex, v => S.CookiesFromBrowser = Browsers[v].Value);
    }

    public string RateLimit
    {
        get => S.RateLimit;
        set
        {
            if (SetProperty(S.RateLimit, (value ?? "").Trim(), S, static (s, v) => s.RateLimit = v))
            {
                Save();
            }
        }
    }

    public string ExtraArguments
    {
        get => S.ExtraArguments;
        set
        {
            if (SetProperty(S.ExtraArguments, value ?? "", S, static (s, v) => s.ExtraArguments = v))
            {
                Save();
            }
        }
    }

    // ----- clipboard ---------------------------------------------------------------------------

    public bool WatchClipboard
    {
        get => S.WatchClipboard;
        set => SetFlag(S.WatchClipboard, value, v => S.WatchClipboard = v);
    }



    public bool AcceptAnyLink
    {
        get => S.LinkFilter == LinkFilterMode.AnyLink;
        set => SetFlag(AcceptAnyLink, value, v => S.LinkFilter = v ? LinkFilterMode.AnyLink : LinkFilterMode.KnownSites);
    }

    /// <summary>Extra domains, one per line.</summary>
    public string ExtraSitesText
    {
        get => string.Join(Environment.NewLine, S.ExtraSites);
        set
        {
            var sites = (value ?? "")
                .Split(['\r', '\n', ',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries)

                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (sites.SequenceEqual(S.ExtraSites, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            S.ExtraSites = sites;
            OnPropertyChanged();
            Save();
        }
    }

    // ----- tools -----------------------------------------------------------------------------------

    public string ToolsDirectoryText =>
        string.IsNullOrWhiteSpace(S.ToolsDirectory) ? $"Automatic ({_settings.ToolsDirectory})" : S.ToolsDirectory;

    public bool HasCustomToolsDirectory => !string.IsNullOrWhiteSpace(S.ToolsDirectory);

    public int ChannelIndex
    {
        get => (int)S.YtDlpChannel;
        set => SetIndex(value, ChannelOptions.Length, (int)S.YtDlpChannel, v => S.YtDlpChannel = (UpdateChannel)v);
    }

    public bool CheckForToolUpdatesOnStartup
    {
        get => S.CheckForToolUpdatesOnStartup;
        set => SetFlag(S.CheckForToolUpdatesOnStartup, value, v => S.CheckForToolUpdatesOnStartup = v);
    }

    public bool InstallToolUpdatesAutomatically
    {
        get => S.InstallToolUpdatesAutomatically;
        set => SetFlag(S.InstallToolUpdatesAutomatically, value, v => S.InstallToolUpdatesAutomatically = v);
    }

    public string DataDirectory => _settings.DataDirectory;

    // ----- commands ----------------------------------------------------------------------------------

    [RelayCommand]
    private async Task BrowseOutputDirectoryAsync()
    {
        if (await _pickers.PickFolderAsync() is { } folder)
        {
            OutputDirectory = folder;
        }
    }

    [RelayCommand]
    private async Task BrowseToolsDirectoryAsync()
    {
        if (await _pickers.PickFolderAsync() is { } folder)
        {
            SetToolsDirectory(folder);
        }
    }

    [RelayCommand]
    private void UseAutomaticToolsDirectory() => SetToolsDirectory(null);

    [RelayCommand]
    private void ResetOutputTemplate() { _settings.ResetOutputTemplate(); OnPropertyChanged(nameof(OutputTemplate)); }

    private void SetToolsDirectory(string? folder)
    {
        if (string.Equals(S.ToolsDirectory ?? "", folder ?? "", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        S.ToolsDirectory = folder ?? "";
        OnPropertyChanged(nameof(ToolsDirectoryText));
        OnPropertyChanged(nameof(HasCustomToolsDirectory));
        Save();
    }

    // ----- helpers -------------------------------------------------------------------------------------

    private void SetFlag(bool current, bool value, Action<bool> assign, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (current == value)
        {
            return;
        }

        assign(value);
        OnPropertyChanged(name);
        Save();
    }

    private void SetIndex(int value, int count, int current, Action<int> assign, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        // ComboBoxes report -1 while their items are being replaced; ignore it.
        if (value < 0 || value >= count || value == current)
        {
            return;
        }

        assign(value);
        OnPropertyChanged(name);
        Save();
    }

    private void Save()
    {
        if (!_settings.Save(out var error))
        {
            _notifications.Show($"Settings could not be saved: {error}", NotificationLevel.Error);
        }
    }


}
