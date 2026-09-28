using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OnlineVideoManager.Contracts;
using OnlineVideoManager.ViewModels.Services;

namespace OnlineVideoManager.ViewModels;

public sealed partial class ClipboardInboxViewModel : ObservableObject
{
    private readonly EngineSession _engine;
    private readonly SettingsService _settings;
    private readonly IClipboardService _clipboard;
    private readonly DownloadsViewModel _downloads;
    private readonly NotificationViewModel _notifications;
    private bool _started;
    public ClipboardInboxViewModel(EngineSession engine, SettingsService settings, IClipboardService clipboard,
        DownloadsViewModel downloads, NotificationViewModel notifications)
    {
        _engine = engine; _settings = settings; _clipboard = clipboard; _downloads = downloads; _notifications = notifications;
        engine.Changed += (_, _) => Refresh();
        Refresh();
    }
    public ObservableCollection<CollectedLinkViewModel> Links { get; } = [];
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WatchingText))]
    public partial bool IsWatching { get; set; }
    public string WatchingText => IsWatching ? "Watching the clipboard" : "Not watching the clipboard";
    public string Summary => Links.Count > 0 ? $"{Links.Count} link(s) collected" : "Copy video links to collect them here.";
    public bool HasLinks => Links.Count > 0;
    partial void OnIsWatchingChanged(bool value)
    {
        if (_settings.Current.WatchClipboard == value) return;
        _settings.Current.WatchClipboard = value;
        if (!_settings.Save(out var error)) _notifications.Show(error!, NotificationLevel.Error);
    }
    private void Refresh()
    {
        var urls = _engine.State.Inbox;
        foreach (var old in Links.Where(l => !urls.Contains(l.Url)).ToArray()) Links.Remove(old);
        foreach (var url in urls) if (!Links.Any(l => l.Url == url)) Links.Add(new(this, url));
        IsWatching = _settings.Current.WatchClipboard;
        OnPropertyChanged(nameof(Summary)); OnPropertyChanged(nameof(HasLinks));
        AddSelectedCommand.NotifyCanExecuteChanged(); ClearCommand.NotifyCanExecuteChanged();
    }
    public async Task StartAsync()
    {
        if (_started) return;
        _started = true;
        _clipboard.ContentChanged += OnClipboardChanged;
        if (IsWatching) await CheckClipboardAsync();
    }
    public void Stop() { _clipboard.ContentChanged -= OnClipboardChanged; _started = false; }
    private async void OnClipboardChanged(object? sender, EventArgs e)
    {
        try { if (IsWatching) await CheckClipboardAsync(); }
        catch (Exception ex) { _notifications.Show(ex.Message, NotificationLevel.Warning); }
    }
    private async Task CheckClipboardAsync()
    {
        if (await _clipboard.ReadAsync() is { } data)
            await _engine.SendAsync(new("clipboard.collect", Text: data.Text, Html: data.Html));
    }
    [RelayCommand]
    private async Task ScanHistoryAsync()
    {
        var history = await _clipboard.ReadHistoryAsync();
        if (history.Status != ClipboardHistoryStatus.Success)
        {
            _notifications.Show(history.Status == ClipboardHistoryStatus.Disabled
                ? "Enable Windows clipboard history with Win+V to scan earlier copies."
                : "Windows could not provide clipboard history. Try with this window in front.", NotificationLevel.Warning);
            return;
        }
        foreach (var data in history.Items.Reverse())
            await _engine.SendAsync(new("clipboard.collect", Text: data.Text, Html: data.Html));
    }
    [RelayCommand(CanExecute = nameof(HasLinks))]
    private Task AddSelectedAsync() => _engine.SendAsync(new("inbox.queue",
        Urls: Links.Where(l => l.IsSelected).Select(l => l.Url).ToArray(), Mode: _downloads.AddMode));
    [RelayCommand(CanExecute = nameof(HasLinks))] private Task ClearAsync() => _engine.SendAsync(new("inbox.clear"));
    [RelayCommand]
    private void SelectAll()
    {
        var selected = Links.Any(l => !l.IsSelected);
        foreach (var link in Links) link.IsSelected = selected;
    }
    internal Task RemoveAsync(CollectedLinkViewModel link) => _engine.SendAsync(new("inbox.remove", Urls: [link.Url]));
}
public sealed partial class CollectedLinkViewModel : ObservableObject
{
    private readonly ClipboardInboxViewModel _owner;
    internal CollectedLinkViewModel(ClipboardInboxViewModel owner, string url) { _owner = owner; Url = url; }
    public string Url { get; }
    public string Host => Formatting.Host(Url);
    [ObservableProperty] public partial bool IsSelected { get; set; } = true;
    [RelayCommand] private Task RemoveAsync() => _owner.RemoveAsync(this);
}
