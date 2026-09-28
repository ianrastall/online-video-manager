using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OnlineVideoManager.Core.Links;
using OnlineVideoManager.Core.Downloads;
using OnlineVideoManager.Core.Settings;
using OnlineVideoManager.ViewModels.Services;

namespace OnlineVideoManager.ViewModels;

/// <summary>
/// Collects video links as they are copied anywhere in Windows, so links gathered one at a
/// time can be queued together. Can also sweep Windows clipboard history (Win+V) for links
/// copied before the app was open.
/// </summary>
public sealed partial class ClipboardInboxViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly IClipboardService _clipboard;
    private readonly DownloadsViewModel _downloads;
    private readonly NotificationViewModel _notifications;
    private bool _started;
    private bool _restoring;
    private readonly QueueStore _inboxStore;

    public ClipboardInboxViewModel(SettingsService settings, IClipboardService clipboard, DownloadsViewModel downloads, NotificationViewModel notifications)
    {
        _settings = settings;
        _clipboard = clipboard;
        _downloads = downloads;
        _notifications = notifications;
        _inboxStore = new QueueStore(Path.Combine(settings.DataDirectory, "inbox.json"));
        IsWatching = settings.Current.WatchClipboard;
        Links.CollectionChanged += OnLinksChanged;
        UpdateSummary();
    }

    public ObservableCollection<CollectedLinkViewModel> Links { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WatchingText))]
    public partial bool IsWatching { get; set; }

    public string WatchingText => IsWatching ? "Watching the clipboard" : "Not watching the clipboard";

    [ObservableProperty]
    public partial string Summary { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddSelectedCommand), nameof(ClearCommand))]
    public partial bool HasLinks { get; set; }

    partial void OnIsWatchingChanged(bool value)
    {
        if (_settings.Current.WatchClipboard != value)
        {
            _settings.Current.WatchClipboard = value;
            _settings.Save(out _);
        }

        UpdateSummary();
    }

    /// <summary>Begin listening, and look at whatever is on the clipboard right now.</summary>
    public async Task StartAsync()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _restoring = true;
        try
        {
            foreach (var entry in _inboxStore.Load())
                if (!_downloads.Contains(entry.Url) && !Links.Any(l => LinkKey.For(l.Url) == LinkKey.For(entry.Url)))
                    Links.Add(new CollectedLinkViewModel(this, entry.Url));
        }
        finally { _restoring = false; }
        _clipboard.ContentChanged += OnClipboardChanged;
        _settings.Changed += (_, _) => IsWatching = _settings.Current.WatchClipboard;
        if (IsWatching)
        {
            await CheckClipboardAsync();
        }
    }

    private async void OnClipboardChanged(object? sender, EventArgs e)
    {
        try
        {
            if (IsWatching)
            {
                await CheckClipboardAsync();
            }
        }
        catch (Exception ex)
        {
            // An event handler must not throw; a failed read only means this copy is missed.
            System.Diagnostics.Debug.WriteLine($"Clipboard check failed: {ex}");
        }
    }

    private async Task CheckClipboardAsync()
    {
        var snapshot = await _clipboard.ReadAsync();
        if (snapshot is not null)
        {
            Collect([snapshot], announce: false);
        }
    }

    [RelayCommand]
    private async Task ScanHistoryAsync()
    {
        var result = await _clipboard.ReadHistoryAsync();
        switch (result.Status)
        {
            case ClipboardHistoryStatus.Disabled:
                _notifications.Show("Clipboard history is turned off. Press Win+V, or open Settings > System > Clipboard, to turn it on.", NotificationLevel.Warning);
                return;
            case ClipboardHistoryStatus.AccessDenied:
                _notifications.Show("Windows did not allow reading clipboard history. Make sure this window is in front and try again.", NotificationLevel.Warning);
                return;
            case ClipboardHistoryStatus.Unavailable:
                _notifications.Show("Clipboard history is not available on this system.", NotificationLevel.Warning);
                return;
        }

        // History is newest first; collect oldest first so the list keeps the order links were copied.
        var found = Collect(result.Items.Reverse(), announce: true);
        if (found == 0)
        {
            _notifications.Show("No new video links in clipboard history.", NotificationLevel.Info);
        }
    }

    /// <summary>Add qualifying links from clipboard entries. Returns how many were new.</summary>
    public int Collect(IEnumerable<ClipboardSnapshot> snapshots, bool announce)
    {
        var filter = LinkFilter.From(_settings.Current);
        var known = Links.Select(l => LinkKey.For(l.Url)).ToHashSet(StringComparer.Ordinal);
        var fresh = new List<string>();
        foreach (var snapshot in snapshots)
        {
            foreach (var url in filter.Apply(LinkExtractor.FromClipboard(snapshot.Text, snapshot.Html)))
            {
                if (known.Add(LinkKey.For(url)) && !_downloads.Contains(url))
                {
                    fresh.Add(url);
                }
            }
        }

        if (fresh.Count == 0)
        {
            return 0;
        }

        foreach (var url in fresh)
        {
            Links.Add(new CollectedLinkViewModel(this, url));
        }

        if (announce)
        {
            _notifications.Show($"Collected {Formatting.Plural(fresh.Count, "new link")}.", NotificationLevel.Success);
        }

        return fresh.Count;
    }

    [RelayCommand(CanExecute = nameof(HasLinks))]
    private void AddSelected()
    {
        var chosen = Links.Where(l => l.IsSelected).ToList();
        if (chosen.Count == 0)
        {
            _notifications.Show("No links are ticked.", NotificationLevel.Info);
            return;
        }

        var result = _downloads.Enqueue(chosen.Select(l => l.Url), _downloads.AddMode);
        foreach (var link in chosen)
        {
            Links.Remove(link);
        }

        _downloads.Report(result, " from the clipboard");
    }

    [RelayCommand(CanExecute = nameof(HasLinks))]
    private void Clear() => Links.Clear();

    [RelayCommand]
    private void SelectAll()
    {
        var select = Links.Any(l => !l.IsSelected);
        foreach (var link in Links)
        {
            link.IsSelected = select;
        }
    }

    internal void Remove(CollectedLinkViewModel link) => Links.Remove(link);

    private void OnLinksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateSummary();
        if (_restoring) return;
        try { _inboxStore.Save(Links.Select(l => new SavedDownload(l.Url, DownloadMode.Video))); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _notifications.Show($"Could not save collected links: {ex.Message}", NotificationLevel.Warning);
        }
    }

    private void UpdateSummary()
    {
        HasLinks = Links.Count > 0;
        Summary = Links.Count switch
        {
            0 when IsWatching => "Copy video links in any app and they collect here.",
            0 => "Turn on watching to collect links as you copy them.",
            _ => Formatting.Plural(Links.Count, "link") + " collected",
        };
    }
}

public sealed partial class CollectedLinkViewModel : ObservableObject
{
    private readonly ClipboardInboxViewModel _owner;

    internal CollectedLinkViewModel(ClipboardInboxViewModel owner, string url)
    {
        _owner = owner;
        Url = url;
        Host = Formatting.Host(url);
    }

    public string Url { get; }

    public string Host { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = true;

    [RelayCommand]
    private void Remove() => _owner.Remove(this);
}
