namespace OnlineVideoManager.ViewModels;

public sealed class MainViewModel(DownloadsViewModel downloads, ClipboardInboxViewModel inbox,
    ToolsViewModel tools, SettingsViewModel settings, NotificationViewModel notifications,
    SettingsService settingsService, EngineSession engine)
{
    private bool _started;
    private readonly CancellationTokenSource _shutdown = new();
    public DownloadsViewModel Downloads { get; } = downloads;
    public ClipboardInboxViewModel Inbox { get; } = inbox;
    public ToolsViewModel Tools { get; } = tools;
    public SettingsViewModel Settings { get; } = settings;
    public NotificationViewModel Notifications { get; } = notifications;
    public SettingsService SettingsService { get; } = settingsService;
    public async Task StartAsync()
    {
        if (_started) return;
        _started = true;
        await engine.SendAsync(new("initialize"));
        await Inbox.StartAsync();
        _ = RefreshAsync(_shutdown.Token);
    }
    public void Shutdown() { _shutdown.Cancel(); Inbox.Stop(); }
    private async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
            while (await timer.WaitForNextTickAsync(ct))
            {
                var reply = await engine.ExecuteAsync(new("snapshot"));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { Notifications.Show(ex.Message, NotificationLevel.Error); }
    }
}
