using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OnlineVideoManager.Contracts;
using OnlineVideoManager.ViewModels.Services;

namespace OnlineVideoManager.ViewModels;

/// <summary>Projects native queue snapshots and forwards user commands.</summary>
public sealed partial class DownloadsViewModel : ObservableObject
{
    private readonly EngineSession _engine;
    private readonly IPickerService _pickers;
    private readonly IShellService _shell;
    private readonly IClipboardService _clipboard;
    private readonly NotificationViewModel _notifications;
    private DownloadMode _defaultMode;

    public DownloadsViewModel(EngineSession engine, IPickerService pickers, IShellService shell,
        IClipboardService clipboard, NotificationViewModel notifications)
    {
        _engine = engine; _pickers = pickers; _shell = shell; _clipboard = clipboard; _notifications = notifications;
        _defaultMode = engine.State.Settings.DefaultMode;
        AddAsAudio = _defaultMode == DownloadMode.Audio;
        engine.Changed += (_, _) => Refresh();
        Refresh();
    }
    public ObservableCollection<DownloadItemViewModel> Items { get; } = [];
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddFromInputCommand))]
    public partial string InputText { get; set; } = "";
    [ObservableProperty] public partial string InputSummary { get; set; } = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddMode))]
    public partial bool AddAsAudio { get; set; }
    public DownloadMode AddMode => AddAsAudio ? DownloadMode.Audio : DownloadMode.Video;
    public bool IsQueueRunning => _engine.State.Running;
    public string QueueToggleText => IsQueueRunning ? "Pause" : "Start";
    public string QueueToggleGlyph => IsQueueRunning ? "\uE769" : "\uE768";
    public string QueueSummary => Items.Count == 0 ? "Queue is empty" : string.Join(" · ",
        Items.GroupBy(i => i.Status).Select(g => $"{g.Count()} {g.Key.ToString().ToLowerInvariant()}"));
    public bool IsEmpty => Items.Count == 0;
    public string OutputDirectory => _engine.State.Settings.OutputDirectory;

    private void Refresh()
    {
        var rows = _engine.State.Items;
        foreach (var old in Items.Where(i => !rows.Any(r => r.Id == i.Id)).ToArray()) Items.Remove(old);
        foreach (var row in rows)
        {
            var item = Items.FirstOrDefault(i => i.Id == row.Id);
            if (item is null) Items.Add(new(this, row)); else item.Apply(row);
        }
        if (_defaultMode != _engine.State.Settings.DefaultMode)
        {
            _defaultMode = _engine.State.Settings.DefaultMode;
            AddAsAudio = _defaultMode == DownloadMode.Audio;
        }
        OnPropertyChanged((string?)null);
    }
    partial void OnInputTextChanged(string value)
    {
        try
        {
            var count = _engine.Execute(new("links.preview", Text: value)).Count;
            InputSummary = string.IsNullOrWhiteSpace(value) ? "" : count == 0 ? "No links found yet" : $"{count} link(s) ready";
        }
        catch (Exception ex) { InputSummary = ex.Message; }
    }
    private bool CanAddFromInput() => !string.IsNullOrWhiteSpace(InputText);
    [RelayCommand(CanExecute = nameof(CanAddFromInput))]
    private async Task AddFromInputAsync() { await AddTextAsync(InputText); InputText = ""; }
    public Task AddTextAsync(string text) => _engine.SendAsync(new("queue.add", Text: text, Mode: AddMode));
    public Task ImportPathAsync(string path) => _engine.SendAsync(new("queue.import", Path: path, Mode: AddMode));
    [RelayCommand]
    private async Task AddFromClipboardAsync()
    {
        var data = await _clipboard.ReadAsync();
        await _engine.SendAsync(new("queue.add", Text: data?.Text, Html: data?.Html, Mode: AddMode));
    }
    [RelayCommand]
    private async Task ImportFileAsync()
    {
        if (await _pickers.PickTextFileAsync() is { } path) await ImportPathAsync(path);
    }
    [RelayCommand] private Task ToggleQueueAsync() => _engine.SendAsync(new(IsQueueRunning ? "queue.pause" : "queue.start"));
    [RelayCommand] private Task RetryFailedAsync() => _engine.SendAsync(new("queue.retryFailed"));
    [RelayCommand] private Task ClearFinishedAsync() => _engine.SendAsync(new("queue.clearFinished"));
    [RelayCommand] private Task CancelAllAsync() => _engine.SendAsync(new("queue.cancelAll"));
    [RelayCommand]
    private async Task OpenOutputFolderAsync()
    {
        try { _shell.OpenFolder((await _engine.ExecuteAsync(new("folder.prepare", Kind: "output"))).Path!); }
        catch (Exception ex) { _notifications.Show(ex.Message, NotificationLevel.Error); }
    }
    internal Task CancelAsync(DownloadItemViewModel item) => _engine.SendAsync(new("queue.cancel", Id: item.Id));
    internal Task RetryAsync(DownloadItemViewModel item) => _engine.SendAsync(new("queue.retry", Id: item.Id));
    internal Task RemoveAsync(DownloadItemViewModel item) => _engine.SendAsync(new("queue.remove", Id: item.Id));
    internal void OpenFile(DownloadItemViewModel item) => _shell.OpenFile(item.FilePath);
    internal void RevealFile(DownloadItemViewModel item) => _shell.RevealFile(item.FilePath);
    internal void CopyUrl(DownloadItemViewModel item) => _clipboard.SetText(item.Url);
}
