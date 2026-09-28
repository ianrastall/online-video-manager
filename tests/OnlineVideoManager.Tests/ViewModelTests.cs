using OnlineVideoManager.Core.Downloads;
using OnlineVideoManager.Core.Settings;
using OnlineVideoManager.Core.Tools;
using OnlineVideoManager.ViewModels;
using OnlineVideoManager.ViewModels.Services;

namespace OnlineVideoManager.Tests;

public class DownloadsViewModelTests
{
    private const string A = "https://youtu.be/IQb6su5v5xA";
    private const string B = "https://youtu.be/hcoZX8f_yp0";
    private const string C = "https://youtu.be/R-E0HvHfa9o";

    [Fact]
    public void Runs_up_to_concurrency_and_skips_duplicates()
    {
        using var app = new TestApp();

        var result = app.Downloads.Enqueue([A, B, C, "https://www.youtube.com/watch?v=IQb6su5v5xA"], DownloadMode.Video);

        Assert.Equal(new EnqueueResult(3, 1), result);
        Assert.Equal(2, app.Downloads.Items.Count(i => i.Status == DownloadStatus.Running));
        app.Dispatcher.RunUntil(() => app.Runner.Runs.Count == 2);
        // Both start concurrently, so compare as a set.
        Assert.Equal([A, B], app.Runner.Runs.Select(r => r.Request.Arguments[^1]).Order(StringComparer.Ordinal));
        Assert.All(app.Runner.Runs, r => Assert.Equal("--", r.Request.Arguments[^2]));
    }

    [Fact]
    public void Finishing_a_download_starts_the_next_and_records_the_result()
    {
        using var app = new TestApp(configure: s => s.Concurrency = 1);
        app.Downloads.Enqueue([A, B], DownloadMode.Audio);
        app.Dispatcher.RunUntil(() => app.Runner.Runs.Count == 1);

        var first = app.Runner.Runs[0];
        first.OnUpdate(new JobUpdate.Title("First video"));
        first.OnUpdate(new JobUpdate.Progress(50, 100, 10, 5));
        first.Exit.SetResult(0);
        app.Dispatcher.RunUntil(() => app.Runner.Runs.Count == 2);

        var item = app.Downloads.Items[0];
        Assert.Equal(DownloadStatus.Completed, item.Status);
        Assert.Equal("First video", item.DisplayName);
        Assert.Equal(100, item.ProgressValue);
        Assert.Equal(DownloadStatus.Running, app.Downloads.Items[1].Status);

        var second = app.Runner.Runs[1];
        second.OnUpdate(new JobUpdate.Log("ERROR: Video unavailable", true));
        second.Exit.SetResult(1);
        app.Dispatcher.RunUntil(() => app.Downloads.Items[1].Status == DownloadStatus.Failed);
        Assert.Equal("Video unavailable", app.Downloads.Items[1].ErrorMessage);
    }

    [Fact]
    public void Progress_spans_merged_streams()
    {
        using var app = new TestApp();
        app.Downloads.Enqueue([A], DownloadMode.Video);
        app.Dispatcher.RunUntil(() => app.Runner.Runs.Count == 1);
        var run = app.Runner.Runs[0];

        run.OnUpdate(new JobUpdate.Streams(2));
        run.OnUpdate(new JobUpdate.Destination());
        run.OnUpdate(new JobUpdate.Progress(100, 100, null, null));
        run.OnUpdate(new JobUpdate.Destination());
        run.OnUpdate(new JobUpdate.Progress(50, 100, null, null));
        var item = app.Downloads.Items[0];
        app.Dispatcher.RunUntil(() => Math.Abs(item.ProgressValue - 75) < 0.001);
        Assert.Contains("Stream 2 of 2", item.DetailText);
    }

    [Fact]
    public void Cancel_running_and_queued_items()
    {
        using var app = new TestApp(configure: s => s.Concurrency = 1);
        app.Downloads.Enqueue([A, B], DownloadMode.Video);
        app.Dispatcher.RunUntil(() => app.Runner.Runs.Count == 1);

        app.Downloads.Items[1].CancelCommand.Execute(null);
        Assert.Equal(DownloadStatus.Canceled, app.Downloads.Items[1].Status);

        app.Downloads.Items[0].CancelCommand.Execute(null);
        app.Dispatcher.RunUntil(() => app.Downloads.Items[0].Status == DownloadStatus.Canceled);
        Assert.Single(app.Runner.Runs);

        app.Downloads.Items[1].RetryCommand.Execute(null);
        app.Dispatcher.RunUntil(() => app.Runner.Runs.Count == 2);
    }

    [Fact]
    public void Missing_yt_dlp_pauses_the_queue_and_asks_for_tools()
    {
        using var app = new TestApp(installYtDlp: false);
        var asked = false;
        app.Downloads.ToolsMissing += (_, _) => asked = true;

        app.Downloads.Enqueue([A], DownloadMode.Video);

        Assert.True(asked);
        Assert.False(app.Downloads.IsQueueRunning);
        Assert.Equal(DownloadStatus.Queued, app.Downloads.Items[0].Status);
    }

    [Fact]
    public void Nothing_starts_while_tools_are_busy()
    {
        using var app = new TestApp();
        app.Coordinator.SetToolsBusy(true);
        app.Downloads.Enqueue([A], DownloadMode.Video);
        Assert.Equal(DownloadStatus.Queued, app.Downloads.Items[0].Status);

        app.Coordinator.SetToolsBusy(false);
        Assert.Equal(DownloadStatus.Running, app.Downloads.Items[0].Status);
    }

    [Fact]
    public void Queue_survives_restart_paused()
    {
        using var app = new TestApp(configure: s => s.Concurrency = 1);
        app.Downloads.Enqueue([A, B], DownloadMode.Video);
        app.Dispatcher.RunUntil(() => app.Runner.Runs.Count == 1);
        app.Downloads.Shutdown();

        var saved = app.Queue.Load();
        Assert.Equal([A, B], saved.Select(s => s.Url));
        Assert.All(saved, s => Assert.Equal(SavedDownloadState.Queued, s.State));

        var restarted = new DownloadsViewModel(app.Settings, app.Queue, app.Runner, app.Dispatcher, app.Pickers, app.Shell, app.Clipboard, app.Notifications, new WorkCoordinator());
        restarted.RestoreQueue();
        Assert.False(restarted.IsQueueRunning);
        Assert.Equal(2, restarted.Items.Count(i => i.Status == DownloadStatus.Queued));
    }

    [Fact]
    public void Input_box_counts_and_adds_links()
    {
        using var app = new TestApp();
        app.Downloads.IsQueueRunning = false;
        app.Downloads.InputText = $"{A}\n{B}\nsome notes";
        Assert.Equal("2 links ready", app.Downloads.InputSummary);

        app.Downloads.AddFromInputCommand.Execute(null);

        Assert.Equal(2, app.Downloads.Items.Count);
        Assert.Equal("", app.Downloads.InputText);
    }
}

public class ClipboardInboxViewModelTests
{
    [Fact]
    public async Task Collects_qualifying_links_as_they_are_copied()
    {
        using var app = new TestApp();
        await app.Inbox.StartAsync();

        app.Clipboard.Copy("https://youtu.be/IQb6su5v5xA");
        app.Clipboard.Copy("https://example.com/not-a-video");
        app.Clipboard.Copy("https://www.youtube.com/watch?v=IQb6su5v5xA");
        app.Clipboard.Copy("two at once: https://vimeo.com/1 https://vimeo.com/2");

        Assert.Equal(["https://youtu.be/IQb6su5v5xA", "https://vimeo.com/1", "https://vimeo.com/2"], app.Inbox.Links.Select(l => l.Url));
    }

    [Fact]
    public async Task Adding_ticked_links_queues_them_and_empties_the_inbox()
    {
        using var app = new TestApp();
        app.Downloads.IsQueueRunning = false;
        app.Settings.Current.StartQueueAutomatically = false;
        await app.Inbox.StartAsync();
        app.Clipboard.Copy("https://vimeo.com/1\nhttps://vimeo.com/2\nhttps://vimeo.com/3");
        app.Inbox.Links[1].IsSelected = false;

        app.Inbox.AddSelectedCommand.Execute(null);

        Assert.Equal(["https://vimeo.com/1", "https://vimeo.com/3"], app.Downloads.Items.Select(i => i.Url));
        Assert.Equal(["https://vimeo.com/2"], app.Inbox.Links.Select(l => l.Url));

        app.Clipboard.Copy("https://vimeo.com/1");
        Assert.Single(app.Inbox.Links);
    }

    [Fact]
    public async Task Scans_clipboard_history_oldest_first()
    {
        using var app = new TestApp();
        app.Settings.Current.WatchClipboard = false;
        await app.Inbox.StartAsync();
        app.Clipboard.History = new ClipboardHistoryResult(ClipboardHistoryStatus.Success,
        [
            new ClipboardSnapshot("https://vimeo.com/3", null),
            new ClipboardSnapshot("password123", null),
            new ClipboardSnapshot(null, "<a href=\"https://vimeo.com/2\">x</a>"),
            new ClipboardSnapshot("https://vimeo.com/1", null),
        ]);

        await app.Inbox.ScanHistoryCommand.ExecuteAsync(null);

        Assert.Equal(["https://vimeo.com/1", "https://vimeo.com/2", "https://vimeo.com/3"], app.Inbox.Links.Select(l => l.Url));
    }

    [Fact]
    public async Task Clipboard_always_collects_even_with_legacy_auto_queue_setting()
    {
        using var app = new TestApp(configure: s => s.AutoQueueClipboardLinks = true);
        await app.Inbox.StartAsync();

        app.Clipboard.Copy("https://vimeo.com/1");

        Assert.Single(app.Inbox.Links);
        Assert.Empty(app.Downloads.Items);
    }
}

public class ToolsViewModelTests
{
    [Fact]
    public async Task Startup_installs_missing_tools_when_automatic()
    {
        using var app = new TestApp(installYtDlp: false);

        await app.Tools.StartupAsync();

        Assert.Equal([ToolId.YtDlp, ToolId.Deno, ToolId.Ffmpeg], app.ToolManager.InstallCalls);
        Assert.All(app.Tools.Tools, t => Assert.Equal("Up to date", t.StatusText));
        Assert.False(app.Coordinator.ToolsBusy);
    }

    [Fact]
    public async Task Startup_only_reports_when_automatic_installs_are_off()
    {
        using var app = new TestApp(installYtDlp: false, configure: s => s.InstallToolUpdatesAutomatically = false);

        await app.Tools.StartupAsync();

        Assert.Empty(app.ToolManager.InstallCalls);
        Assert.Equal(NotificationLevel.Warning, app.Notifications.Level);
        Assert.Contains("Not installed", app.Notifications.Message);
    }

    [Fact]
    public async Task Detects_outdated_tools()
    {
        using var app = new TestApp(configure: s => s.InstallToolUpdatesAutomatically = false);
        app.ToolManager.Installed[ToolId.YtDlp] = "2026.01.01";
        app.ToolManager.Installed[ToolId.Deno] = "2.5.1";
        app.ToolManager.Installed[ToolId.Ffmpeg] = "N-1-gabc";

        await app.Tools.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.True(app.Tools[ToolId.YtDlp].UpdateAvailable);
        Assert.Equal("Update", app.Tools[ToolId.YtDlp].ActionText);
        Assert.False(app.Tools[ToolId.Deno].UpdateAvailable);
    }

    [Fact]
    public async Task Install_waits_for_running_downloads()
    {
        using var app = new TestApp();
        app.Coordinator.DownloadStarted();

        var install = app.Tools.InstallMissingAsync();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(install.IsCompleted);
        Assert.True(app.Coordinator.ToolsBusy);

        app.Coordinator.DownloadFinished();
        Assert.True(await install);
    }
}

public class SettingsTests
{
    [Fact]
    public void Settings_round_trip_and_tolerate_partial_files()
    {
        var dir = Directory.CreateTempSubdirectory("ovm-settings-").FullName;
        try
        {
            var path = Path.Combine(dir, "settings.json");
            File.WriteAllText(path, """
                { "concurrency": 50, "container": "mp4", // comment
                  "extraSites": [" Example.com ", "example.com"] }
                """);
            var store = new SettingsStore(path);

            var loaded = store.Load();
            Assert.Equal(8, loaded.Concurrency);
            Assert.Equal(VideoContainer.Mp4, loaded.Container);
            Assert.True(loaded.EmbedMetadata);
            Assert.Equal(["Example.com"], loaded.ExtraSites);

            loaded.AudioFormat = AudioFormat.Mp3;
            store.Save(loaded);
            Assert.Contains("\"audioFormat\": \"Mp3\"", File.ReadAllText(path));
            Assert.Equal(AudioFormat.Mp3, store.Load().AudioFormat);

            File.WriteAllText(path, "{ not json");
            Assert.Equal(2, store.Load().Concurrency);
            Assert.True(File.Exists(path + ".invalid"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Settings_view_model_saves_changes()
    {
        using var app = new TestApp();
        var vm = new SettingsViewModel(app.Settings, app.Pickers, app.Notifications);

        vm.MaxHeightIndex = 4;
        vm.ExtraSitesText = "https://www.Example.com/path\nfoo.org";
        vm.CookiesBrowserIndex = 1;

        var saved = new SettingsStore(Path.Combine(app.Root, "settings.json")).Load();
        Assert.Equal(1080, saved.MaxHeight);
        Assert.Equal(["example.com", "foo.org"], saved.ExtraSites);
        Assert.Equal("firefox", saved.CookiesFromBrowser);
    }
}
