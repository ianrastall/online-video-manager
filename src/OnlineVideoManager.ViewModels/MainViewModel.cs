namespace OnlineVideoManager.ViewModels;

/// <summary>Owns the page view models and the start-up and shut-down sequence.</summary>
public sealed class MainViewModel
{
    private bool _started;
    private readonly CancellationTokenSource _shutdown = new();

    public MainViewModel(
        DownloadsViewModel downloads,
        ClipboardInboxViewModel inbox,
        ToolsViewModel tools,
        SettingsViewModel settings,
        NotificationViewModel notifications,
        SettingsService settingsService)
    {
        Downloads = downloads;
        Inbox = inbox;
        Tools = tools;
        Settings = settings;
        Notifications = notifications;
        SettingsService = settingsService;
        Downloads.ToolsMissing += OnToolsMissing;
    }

    public DownloadsViewModel Downloads { get; }

    public ClipboardInboxViewModel Inbox { get; }

    public ToolsViewModel Tools { get; }

    public SettingsViewModel Settings { get; }

    public NotificationViewModel Notifications { get; }

    public SettingsService SettingsService { get; }

    public async Task StartAsync()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        Downloads.RestoreQueue();
        await Inbox.StartAsync();
        await Tools.StartupAsync();
        _ = RefreshToolsDailyAsync(_shutdown.Token);
    }

    public void Shutdown()
    {
        _shutdown.Cancel();
        Downloads.Shutdown();
    }

    private async Task RefreshToolsDailyAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (!SettingsService.Current.CheckForToolUpdatesOnStartup || Tools.IsBusy) continue;
                await Tools.CheckForUpdatesCommand.ExecuteAsync(null);
                if (!ct.IsCancellationRequested && SettingsService.Current.InstallToolUpdatesAutomatically)
                    await Tools.UpdateAllCommand.ExecuteAsync(null);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { Notifications.Show($"Scheduled tool update failed: {ex.Message}", NotificationLevel.Warning); }
    }

    private async void OnToolsMissing(object? sender, EventArgs e)
    {
        try
        {
            if (!SettingsService.Current.InstallToolUpdatesAutomatically)
            {
                Notifications.Show("yt-dlp is not installed. Open Tools and press Install.", NotificationLevel.Warning);
                return;
            }

            Notifications.Show("Installing yt-dlp and its helpers before downloading.", NotificationLevel.Info);
            if (await Tools.InstallMissingAsync())
            {
                Downloads.IsQueueRunning = true;
            }
        }
        catch (Exception ex)
        {
            Notifications.Show($"Could not install tools: {ex.Message}", NotificationLevel.Error);
        }
    }
}
