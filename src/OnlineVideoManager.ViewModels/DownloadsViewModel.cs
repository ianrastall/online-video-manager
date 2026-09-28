using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OnlineVideoManager.Core.Downloads;
using OnlineVideoManager.Core.Links;
using OnlineVideoManager.Core.Settings;
using OnlineVideoManager.Core.Tools;
using OnlineVideoManager.ViewModels.Services;

namespace OnlineVideoManager.ViewModels;

public sealed record EnqueueResult(int Added, int Skipped);

/// <summary>The download queue: adding links, running yt-dlp for them, and persisting what is unfinished.</summary>
public sealed partial class DownloadsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly QueueStore _queueStore;
    private readonly IDownloadRunner _runner;
    private readonly IUiDispatcher _dispatcher;
    private readonly IPickerService _pickers;
    private readonly IShellService _shell;
    private readonly IClipboardService _clipboard;
    private readonly NotificationViewModel _notifications;
    private readonly WorkCoordinator _coordinator;
    private bool _shuttingDown;
    private bool _pumping;

    public DownloadsViewModel(
        SettingsService settings,
        QueueStore queueStore,
        IDownloadRunner runner,
        IUiDispatcher dispatcher,
        IPickerService pickers,
        IShellService shell,
        IClipboardService clipboard,
        NotificationViewModel notifications,
        WorkCoordinator coordinator)
    {
        _settings = settings;
        _queueStore = queueStore;
        _runner = runner;
        _dispatcher = dispatcher;
        _pickers = pickers;
        _shell = shell;
        _clipboard = clipboard;
        _notifications = notifications;
        _coordinator = coordinator;

        AddAsAudio = settings.Current.DefaultMode == DownloadMode.Audio;
        Items.CollectionChanged += OnItemsChanged;
        _coordinator.Changed += (_, _) => PumpQueue();
        var previousDefault = settings.Current.DefaultMode;
        _settings.Changed += (_, _) =>
        {
            if (_settings.Current.DefaultMode != previousDefault)
            {
                previousDefault = _settings.Current.DefaultMode;
                AddAsAudio = previousDefault == DownloadMode.Audio;
            }
            PumpQueue();
        };
        UpdateSummary();
    }

    /// <summary>Raised when a download cannot start because yt-dlp is not installed.
    /// The queue is paused when this fires.</summary>
    public event EventHandler? ToolsMissing;

    public ObservableCollection<DownloadItemViewModel> Items { get; } = [];

    /// <summary>Text box for typing or pasting one or more links.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddFromInputCommand))]
    public partial string InputText { get; set; } = "";

    [ObservableProperty]
    public partial string InputSummary { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddMode))]
    public partial bool AddAsAudio { get; set; }

    public DownloadMode AddMode => AddAsAudio ? DownloadMode.Audio : DownloadMode.Video;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QueueToggleText), nameof(QueueToggleGlyph))]
    public partial bool IsQueueRunning { get; set; }

    public string QueueToggleText => IsQueueRunning ? "Pause" : "Start";

    /// <summary>Segoe Fluent Icons glyph: Pause or Play.</summary>
    public string QueueToggleGlyph => IsQueueRunning ? "" : "";

    [ObservableProperty]
    public partial string QueueSummary { get; set; } = "";

    [ObservableProperty]
    public partial bool IsEmpty { get; set; } = true;

    public string OutputDirectory => _settings.Current.OutputDirectory;

    partial void OnInputTextChanged(string value)
    {
        var count = LinkExtractor.FromText(value).Count;
        InputSummary = count switch
        {
            0 when string.IsNullOrWhiteSpace(value) => "",
            0 => "No links found yet",
            _ => Formatting.Plural(count, "link") + " ready",
        };
    }

    partial void OnIsQueueRunningChanged(bool value)
    {
        if (value)
        {
            PumpQueue();
        }
    }

    // ----- adding ---------------------------------------------------------------

    private bool CanAddFromInput() => !string.IsNullOrWhiteSpace(InputText);

    [RelayCommand(CanExecute = nameof(CanAddFromInput))]
    private void AddFromInput()
    {
        var links = LinkExtractor.FromText(InputText);
        if (links.Count == 0)
        {
            _notifications.Show("No http or https links were found in the text.", NotificationLevel.Warning);
            return;
        }

        var result = Enqueue(links, AddMode);
        InputText = "";
        Report(result, "");
    }

    [RelayCommand]
    private async Task AddFromClipboardAsync()
    {
        var snapshot = await _clipboard.ReadAsync();
        var links = LinkExtractor.FromText(snapshot?.Text);
        if (links.Count == 0)
        {
            // A copied piece of a web page: only take links that look like videos, not every nav link.
            links = LinkFilter.From(_settings.Current).Apply(LinkExtractor.FromHtml(snapshot?.Html));
        }

        if (links.Count == 0)
        {
            _notifications.Show("The clipboard holds no links.", NotificationLevel.Warning);
            return;
        }

        Report(Enqueue(links, AddMode), " from the clipboard");
    }

    [RelayCommand]
    private async Task ImportFileAsync()
    {
        var path = await _pickers.PickTextFileAsync();
        if (path is null)
        {
            return;
        }

        string text;
        try
        {
            text = await File.ReadAllTextAsync(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _notifications.Show($"Could not read {Path.GetFileName(path)}: {ex.Message}", NotificationLevel.Error);
            return;
        }

        var links = LinkExtractor.FromText(text);
        if (links.Count == 0)
        {
            _notifications.Show($"{Path.GetFileName(path)} contains no links.", NotificationLevel.Warning);
            return;
        }

        Report(Enqueue(links, AddMode), $" from {Path.GetFileName(path)}");
    }

    /// <summary>Add links to the queue. Links already waiting or downloading are skipped.</summary>
    public EnqueueResult Enqueue(IEnumerable<string> urls, DownloadMode mode)
    {
        var pending = Items.Where(i => i.Status is DownloadStatus.Queued or DownloadStatus.Running)
            .Select(i => LinkKey.For(i.Url))
            .ToHashSet(StringComparer.Ordinal);
        int added = 0, skipped = 0;
        foreach (var url in urls)
        {
            if (!pending.Add(LinkKey.For(url)))
            {
                skipped++;
                continue;
            }

            Items.Add(new DownloadItemViewModel(this, url, mode));
            added++;
        }

        if (added > 0)
        {
            SaveQueue();
            if (_settings.Current.StartQueueAutomatically)
            {
                IsQueueRunning = true;
            }

            PumpQueue();
        }

        return new EnqueueResult(added, skipped);
    }

    /// <summary>True if the link is anywhere in the list (any state).</summary>
    public bool Contains(string url)
    {
        var key = LinkKey.For(url);
        return Items.Any(i => LinkKey.For(i.Url) == key);
    }

    public void Report(EnqueueResult result, string source)
    {
        var message = result switch
        {
            { Added: 0, Skipped: > 0 } => $"Already in the queue{source}: {Formatting.Plural(result.Skipped, "link")}.",
            { Skipped: 0 } => $"Added {Formatting.Plural(result.Added, "link")}{source}.",
            _ => $"Added {Formatting.Plural(result.Added, "link")}{source}; {result.Skipped} already queued.",
        };
        _notifications.Show(message, result.Added > 0 ? NotificationLevel.Success : NotificationLevel.Info);
    }

    // ----- queue control ------------------------------------------------------------

    [RelayCommand]
    private void ToggleQueue() => IsQueueRunning = !IsQueueRunning;

    [RelayCommand]
    private void RetryFailed()
    {
        var failed = Items.Where(i => i.Status is DownloadStatus.Failed or DownloadStatus.Canceled).ToList();
        foreach (var item in failed)
        {
            item.ResetForQueue();
        }

        if (failed.Count > 0)
        {
            IsQueueRunning = true;
            SaveQueue();
            PumpQueue();
        }
    }

    [RelayCommand]
    private void ClearFinished()
    {
        foreach (var item in Items.Where(i => i.Status is DownloadStatus.Completed or DownloadStatus.Canceled).ToList())
        {
            Items.Remove(item);
        }

        SaveQueue();
    }

    [RelayCommand]
    private void CancelAll()
    {
        foreach (var item in Items.Where(i => i.CanCancel).ToList())
        {
            Cancel(item);
        }
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        try
        {
            Directory.CreateDirectory(_settings.Current.OutputDirectory);
            _shell.OpenFolder(_settings.Current.OutputDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _notifications.Show($"Could not open the download folder: {ex.Message}", NotificationLevel.Error);
        }
    }

    internal void Cancel(DownloadItemViewModel item)
    {
        switch (item.Status)
        {
            case DownloadStatus.Running when item.Cancellation is { } cts:
                item.IsCanceling = true;
                cts.Cancel();
                break;
            case DownloadStatus.Queued:
                item.MarkCanceled();
                SaveQueue();
                break;
        }
    }

    internal void Retry(DownloadItemViewModel item)
    {
        if (!item.CanRetry)
        {
            return;
        }

        item.ResetForQueue();
        IsQueueRunning = true;
        SaveQueue();
        PumpQueue();
    }

    internal void Remove(DownloadItemViewModel item)
    {
        if (item.Status == DownloadStatus.Running)
        {
            item.Cancellation?.Cancel();
        }

        Items.Remove(item);
        SaveQueue();
    }

    internal void OpenFile(DownloadItemViewModel item)
    {
        if (item.FilePath is { } path && File.Exists(path))
        {
            _shell.OpenFile(path);
        }
        else
        {
            _notifications.Show("The downloaded file is no longer there.", NotificationLevel.Warning);
        }
    }

    internal void RevealFile(DownloadItemViewModel item)
    {
        if (item.FilePath is { } path && File.Exists(path))
        {
            _shell.RevealFile(path);
        }
        else
        {
            OpenOutputFolder();
        }
    }

    internal void CopyUrl(DownloadItemViewModel item) => _clipboard.SetText(item.Url);

    // ----- running -------------------------------------------------------------------

    /// <summary>Start waiting items up to the concurrency limit.</summary>
    public void PumpQueue()
    {
        // Starting a download raises WorkCoordinator.Changed, which calls back in here.
        if (!IsQueueRunning || _coordinator.ToolsBusy || _shuttingDown || _pumping)
        {
            return;
        }

        _pumping = true;
        try
        {
            var limit = Math.Max(1, _settings.Current.Concurrency);
            while (IsQueueRunning
                && Items.Count(i => i.Status == DownloadStatus.Running) < limit
                && Items.FirstOrDefault(i => i.Status == DownloadStatus.Queued) is { } next)
            {
                var ytDlp = ToolId.YtDlp.ExecutablePath(_settings.ToolsDirectory);
                if (!File.Exists(ytDlp) || !File.Exists(ToolId.Deno.ExecutablePath(_settings.ToolsDirectory))
                    || !File.Exists(ToolId.Ffmpeg.ExecutablePath(_settings.ToolsDirectory))
                    || !File.Exists(ToolIdExtensions.FfprobePath(_settings.ToolsDirectory)))
                {
                    IsQueueRunning = false;
                    _notifications.Show("A required tool is missing. Install yt-dlp, Deno, and FFmpeg with ffprobe before downloading.", NotificationLevel.Warning);
                    ToolsMissing?.Invoke(this, EventArgs.Empty);
                    return;
                }

                Start(next, ytDlp);
            }
        }
        finally
        {
            _pumping = false;
        }
    }

    private void Start(DownloadItemViewModel item, string ytDlp)
    {
        var settings = _settings.Current;
        try
        {
            Directory.CreateDirectory(settings.OutputDirectory);
            DiskSpace.Ensure(settings.OutputDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            item.Fail($"Cannot create the download folder {settings.OutputDirectory}: {ex.Message}");
            IsQueueRunning = false;
            _notifications.Show(ex.Message, NotificationLevel.Warning);
            SaveQueue();
            return;
        }

        var toolsDirectory = _settings.ToolsDirectory;
        var request = new DownloadRequest(ytDlp, toolsDirectory, YtDlpArguments.Build(settings, toolsDirectory, item.Mode, item.Url), settings.OutputDirectory);
        var cts = new CancellationTokenSource();
        item.Cancellation = cts;
        item.MarkRunning();
        _coordinator.DownloadStarted();
        UpdateSummary();
        _ = RunAsync(item, request, cts);
    }

    private async Task RunAsync(DownloadItemViewModel item, DownloadRequest request, CancellationTokenSource cts)
    {
        int? exitCode = null;
        string? failure = null;
        var canceled = false;
        try
        {
            exitCode = await Task.Run(() => _runner.RunAsync(request, update => _dispatcher.Post(() => item.Apply(update)), cts.Token)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }
        catch (Exception ex)
        {
            failure = $"Could not run yt-dlp: {ex.Message}";
        }

        _dispatcher.Post(() =>
        {
            item.Cancellation = null;
            cts.Dispose();
            if (canceled)
            {
                item.MarkCanceled();
            }
            else if (failure is not null)
            {
                item.Fail(failure);
            }
            else
            {
                item.Complete(exitCode ?? -1);
            }

            _coordinator.DownloadFinished();
            UpdateSummary();
            if (!_shuttingDown)
            {
                SaveQueue();
                PumpQueue();
                ReportIfAllDone(item);
            }
        });
    }

    private void ReportIfAllDone(DownloadItemViewModel last)
    {
        if (Items.Any(i => i.Status is DownloadStatus.Queued or DownloadStatus.Running) || last.Status == DownloadStatus.Canceled)
        {
            return;
        }

        var failed = Items.Count(i => i.Status == DownloadStatus.Failed);
        if (failed > 0)
        {
            _notifications.Show($"Queue finished; {Formatting.Plural(failed, "download")} failed. Open the log on a failed item to see why.", NotificationLevel.Warning);
        }
        else
        {
            _notifications.Show("All downloads finished.", NotificationLevel.Success);
        }
    }

    // ----- persistence -------------------------------------------------------------

    /// <summary>Bring back unfinished entries from the last session. The queue starts paused
    /// if any are waiting, so nothing downloads unannounced at launch.</summary>
    public void RestoreQueue()
    {
        var saved = _queueStore.Load();
        foreach (var entry in saved)
        {
            if (Contains(entry.Url))
            {
                continue;
            }

            var item = new DownloadItemViewModel(this, entry.Url, entry.Mode);
            item.Restore(entry.State, entry.Error);
            Items.Add(item);
        }

        var waiting = Items.Count(i => i.Status == DownloadStatus.Queued);
        if (waiting > 0)
        {
            IsQueueRunning = false;
            _notifications.Show($"Restored {Formatting.Plural(waiting, "unfinished download")}. Press Start to resume.", NotificationLevel.Info);
        }
    }

    public void SaveQueue()
    {
        UpdateSummary();
        var entries = Items
            .Where(i => i.Status != DownloadStatus.Completed)
            .Select(i => i.Status switch
            {
                DownloadStatus.Failed => new SavedDownload(i.Url, i.Mode, SavedDownloadState.Failed, i.ErrorMessage),
                DownloadStatus.Canceled => new SavedDownload(i.Url, i.Mode, SavedDownloadState.Canceled),
                _ => new SavedDownload(i.Url, i.Mode),
            });
        try
        {
            _queueStore.Save(entries);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _notifications.Show($"Could not save the queue: {ex.Message}", NotificationLevel.Warning);
        }
    }

    /// <summary>Called when the window closes: save the queue (running items are saved as
    /// waiting, and yt-dlp resumes their partial files next time), then stop yt-dlp.</summary>
    public void Shutdown()
    {
        SaveQueue();
        _shuttingDown = true;
        foreach (var item in Items.Where(i => i.Status == DownloadStatus.Running))
        {
            item.Cancellation?.Cancel();
        }
    }

    // ----- summary ---------------------------------------------------------------------

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateSummary();

    private void UpdateSummary()
    {
        IsEmpty = Items.Count == 0;
        int running = 0, waiting = 0, done = 0, failed = 0;
        foreach (var item in Items)
        {
            switch (item.Status)
            {
                case DownloadStatus.Running: running++; break;
                case DownloadStatus.Queued: waiting++; break;
                case DownloadStatus.Completed: done++; break;
                case DownloadStatus.Failed: failed++; break;
            }
        }

        var parts = new List<string>(4);
        if (running > 0)
        {
            parts.Add($"{running} downloading");
        }

        if (waiting > 0)
        {
            parts.Add($"{waiting} waiting");
        }

        if (done > 0)
        {
            parts.Add($"{done} done");
        }

        if (failed > 0)
        {
            parts.Add($"{failed} failed");
        }

        QueueSummary = parts.Count > 0 ? string.Join(" · ", parts) : "Queue is empty";
    }
}
