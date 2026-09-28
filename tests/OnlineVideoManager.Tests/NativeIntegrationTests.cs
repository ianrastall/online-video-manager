using System.Text;
using OnlineVideoManager.Contracts;
using OnlineVideoManager.Interop;
using OnlineVideoManager.ViewModels;
using OnlineVideoManager.ViewModels.Services;

namespace OnlineVideoManager.Tests;

public sealed class NativeIntegrationTests
{
    [Fact]
    public void QueueAndInboxSurviveNativeEngineRestart()
    {
        using var home = new TestHome();
        using (var engine = home.Open())
        {
            var state = engine.Execute(new("snapshot")).State!;
            Assert.False(state.Running);
            Assert.Equal(VideoCodec.Best, state.Settings.VideoCodec);
            Assert.Equal(0, state.Settings.MaxHeight);
            var reply = engine.Execute(new("queue.add", Text: "https://youtu.be/abcdefghijk https://youtube.com/watch?v=abcdefghijk&t=2"));
            Assert.Equal(1, reply.Added);
            engine.Execute(new("queue.cancel", Id: reply.State!.Items.Single().Id));
            engine.Execute(new("clipboard.collect", Text: "https://vimeo.com/3"));
        }
        using var restored = home.Open();
        var snapshot = restored.Execute(new("snapshot")).State!;
        Assert.Equal(DownloadStatus.Canceled, snapshot.Items.Single().Status);
        Assert.Single(snapshot.Inbox);
        Assert.False(snapshot.Running);
    }

    [Fact]
    public async Task ClipboardRemainsCollectOnlyWithAutomaticQueueEnabled()
    {
        using var home = new TestHome();
        using var engine = home.Open();
        var notifications = new NotificationViewModel();
        var session = new EngineSession(engine, notifications);
        var settings = new SettingsService(session);
        settings.Current.StartQueueAutomatically = true;
        Assert.True(settings.Save(out _));
        var clipboard = new TestPlatform { Current = new("https://vimeo.com/21", null) };
        var downloads = new DownloadsViewModel(session, clipboard, clipboard, clipboard, notifications);
        var inbox = new ClipboardInboxViewModel(session, settings, clipboard, downloads, notifications);
        await inbox.StartAsync();
        Assert.Single(inbox.Links);
        Assert.Empty(downloads.Items);
        Assert.False(session.State.Running);
        session.Execute(new("clipboard.collect", Text: "https://youtube.com.evil.test/watch?v=abcdefghijk"));
        Assert.Single(inbox.Links);
        inbox.Stop();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnicodeTextFileIsImportedByNativeEngine(bool utf16)
    {
        using var home = new TestHome();
        var file = Path.Combine(home.Path, "日本語 links.txt");
        await File.WriteAllTextAsync(file, "https://vimeo.com/1\nhttps://vimeo.com/2",
            utf16 ? Encoding.Unicode : new UTF8Encoding(true), TestContext.Current.CancellationToken);
        using var engine = home.Open();
        var reply = await engine.ExecuteAsync(new("queue.import", Path: file, Mode: DownloadMode.Audio));
        Assert.Equal(2, reply.Added);
        Assert.All(reply.State!.Items, i => Assert.Equal(DownloadMode.Audio, i.Mode));
        Assert.False(reply.State.Running);
    }

    [Fact]
    public async Task ViewModelsForwardCommandsAndPreserveSelection()
    {
        using var home = new TestHome();
        using var engine = home.Open();
        var notifications = new NotificationViewModel();
        var session = new EngineSession(engine, notifications);
        var platform = new TestPlatform();
        var settings = new SettingsService(session);
        var downloads = new DownloadsViewModel(session, platform, platform, platform, notifications);
        var inbox = new ClipboardInboxViewModel(session, settings, platform, downloads, notifications);
        session.Execute(new("clipboard.collect", Text: "https://vimeo.com/1 https://vimeo.com/2"));
        inbox.Links[0].IsSelected = false;
        session.Execute(new("clipboard.collect", Text: "https://vimeo.com/3"));
        Assert.False(inbox.Links[0].IsSelected);
        await inbox.AddSelectedCommand.ExecuteAsync(null);
        Assert.Equal(2, downloads.Items.Count);
        Assert.Single(inbox.Links);
        await downloads.Items[0].CancelCommand.ExecuteAsync(null);
        Assert.Equal(DownloadStatus.Canceled, downloads.Items[0].Status);
        await downloads.ClearFinishedCommand.ExecuteAsync(null);
        Assert.Single(downloads.Items);
        downloads.InputText = "https://vimeo.com/4";
        Assert.Contains("1 link", downloads.InputSummary);
    }

    [Fact]
    public void NativeSettingsNormalizeAndRejectFailedWrites()
    {
        using var home = new TestHome();
        using var engine = home.Open();
        var session = new EngineSession(engine, new());
        var settings = new SettingsService(session);
        settings.Current.Concurrency = 99;
        settings.Current.ExtraSites = ["https://www.EXAMPLE.org/clip", "example.org"];
        Assert.True(settings.Save(out _));
        Assert.Equal(8, settings.Current.Concurrency);
        Assert.Equal(["example.org"], settings.Current.ExtraSites);
        var original = settings.Current.OutputTemplate;
        settings.Current.OutputTemplate = "custom.%(ext)s";
        Assert.True(settings.Save(out _));
        settings.ResetOutputTemplate();
        Assert.Equal(original, settings.Current.OutputTemplate);
        using var locked = new FileStream(System.IO.Path.Combine(home.Path, "settings.json"), FileMode.Open, FileAccess.Read, FileShare.None);
        settings.Current.Concurrency = 1;
        Assert.False(settings.Save(out var error));
        Assert.NotEmpty(error!);
        Assert.Equal(8, settings.Current.Concurrency);
    }

    [Fact]
    public void InvalidCommandsAndExclusiveOwnershipReturnErrors()
    {
        using var home = new TestHome();
        using var engine = home.Open();
        Assert.Throws<InvalidOperationException>(() => engine.Execute(new("bad.operation")));
        Assert.Throws<InvalidOperationException>(() => new NativeEngine(home.Path));
        Assert.Throws<InvalidOperationException>(() => engine.Execute(new("queue.add", Text: "no URL")));
        engine.Dispose();
        Assert.Throws<ObjectDisposedException>(() => engine.Execute(new("snapshot")));
    }

    [Fact]
    public async Task ConcurrentAbiCallsCannotLoseQueueEntries()
    {
        using var home = new TestHome();
        using var engine = home.Open();
        await Task.WhenAll(Enumerable.Range(1, 30).Select(i => engine.ExecuteAsync(new("queue.add", Text: $"https://vimeo.com/{i}"))));
        Assert.Equal(30, engine.Execute(new("snapshot")).State!.Items.Length);
    }

    [Fact]
    public void CorruptStateIsPreservedAndDefaultsRecovered()
    {
        using var home = new TestHome();
        File.WriteAllText(System.IO.Path.Combine(home.Path, "settings.json"), "{bad");
        using var engine = new NativeEngine(home.Path);
        Assert.Equal(2, engine.Execute(new("snapshot")).State!.Settings.Concurrency);
        Assert.Single(Directory.GetFiles(home.Path, "settings.json.invalid.*"));
    }

    [Fact]
    public async Task MissingToolsPauseQueueAndSurfaceActionableNotice()
    {
        using var home = new TestHome();
        using var engine = home.Open();
        engine.Execute(new("queue.add", Text: "https://vimeo.com/1"));
        engine.Execute(new("queue.start"));
        EngineSnapshot state = engine.Execute(new("snapshot")).State!;
        for (var i = 0; i < 100 && state.Running; i++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
            state = engine.Execute(new("snapshot")).State!;
        }
        Assert.False(state.Running);
        Assert.Contains("required tool is missing", state.Notice);
        Assert.Equal(DownloadStatus.Queued, state.Items.Single().Status);
    }
}

internal sealed class TestHome : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("ovm-abi-").FullName;
    public NativeEngine Open()
    {
        var engine = new NativeEngine(Path);
        var s = engine.Execute(new("snapshot")).State!.Settings;
        s.CheckForToolUpdatesOnStartup = false;
        s.InstallToolUpdatesAutomatically = false;
        s.WatchClipboard = true;
        engine.Execute(new("settings.set", Settings: s));
        return engine;
    }
    public void Dispose() => Directory.Delete(Path, true);
}

internal sealed class TestPlatform : IClipboardService, IPickerService, IShellService
{
    public ClipboardSnapshot? Current { get; set; }
    public event EventHandler? ContentChanged { add { } remove { } }
    public Task<ClipboardSnapshot?> ReadAsync() => Task.FromResult(Current);
    public Task<ClipboardHistoryResult> ReadHistoryAsync() => Task.FromResult(new ClipboardHistoryResult(ClipboardHistoryStatus.Success, []));
    public void SetText(string text) { }
    public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);
    public Task<string?> PickTextFileAsync() => Task.FromResult<string?>(null);
    public void OpenFolder(string path) { }
    public void RevealFile(string path) { }
    public void OpenFile(string path) { }
}
