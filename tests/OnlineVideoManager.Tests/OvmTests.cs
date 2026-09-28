using System.Text.Json;
using OnlineVideoManager.Core.Downloads;
using OnlineVideoManager.Core.Interop;
using OnlineVideoManager.Core.Settings;

namespace OnlineVideoManager.Tests;

public class OvmTests
{
    [Fact]
    public async Task Inbox_survives_restart_without_queueing()
    {
        using var app = new TestApp();
        await app.Inbox.StartAsync();
        app.Clipboard.Copy("https://vimeo.com/1234");
        var restarted = new ViewModels.ClipboardInboxViewModel(app.Settings, app.Clipboard, app.Downloads, app.Notifications);
        await restarted.StartAsync();
        Assert.Equal("https://vimeo.com/1234", Assert.Single(restarted.Links).Url);
        Assert.Empty(app.Downloads.Items);
    }

    [Fact]
    public void Fresh_queue_waits_for_explicit_start()
    {
        Assert.False(new AppSettings().StartQueueAutomatically);
        using var app = new TestApp(configure: s => s.StartQueueAutomatically = false);
        app.Downloads.Enqueue(["https://vimeo.com/1"], DownloadMode.Video);
        Assert.Empty(app.Runner.Runs);
        Assert.False(app.Downloads.IsQueueRunning);
        app.Downloads.ToggleQueueCommand.Execute(null);
        app.Dispatcher.RunUntil(() => app.Runner.Runs.Count == 1);
    }

    [Fact]
    public void Disk_reserve_blocks_low_space_and_accepts_boundary()
    {
        Assert.Throws<IOException>(() => DiskSpace.Validate(1023, 1024));
        DiskSpace.Validate(1024, 1024);
    }

    [Theory]
    [InlineData(VideoCodec.H264, "avc1")]
    [InlineData(VideoCodec.Hevc, "hev1|hvc1|hevc")]
    [InlineData(VideoCodec.Av1, "av01")]
    [InlineData(VideoCodec.Vp9, "vp0?9")]
    public void Codec_selection_preserves_best_matching_source(VideoCodec codec, string prefix)
    {
        var a = YtDlpArguments.Build(new() { VideoCodec = codec, Container = VideoContainer.Mp4 }, ".", DownloadMode.Video, "https://vimeo.com/1");
        Assert.Equal($"bv*[vcodec~='^({prefix})']+ba/b[vcodec~='^({prefix})']", a[a.IndexOf("-f") + 1]);
        Assert.Equal("mp4", a[a.IndexOf("--remux-video") + 1]);
        Assert.Contains("--ignore-config", a);
    }

    [Fact]
    public void Native_events_round_trip_all_polymorphic_updates()
    {
        JobUpdate[] updates = [new JobUpdate.Title("日本語 🎵"), new JobUpdate.Streams(2), new JobUpdate.Destination(),
            new JobUpdate.Progress(1024, null, 256, null), new JobUpdate.PlaylistItem(1, 3), new JobUpdate.Stage("Merger"),
            new JobUpdate.FileSaved("C:\\Videos\\é.mp4"), new JobUpdate.Log("failure", true)];
        foreach (var update in updates)
        {
            var json = JsonSerializer.Serialize(new CoreEvent(Download: update), ProtocolJson.Default.CoreEvent);
            Assert.Equal(update, JsonSerializer.Deserialize(json, ProtocolJson.Default.CoreEvent)!.Download);
        }
    }
}
