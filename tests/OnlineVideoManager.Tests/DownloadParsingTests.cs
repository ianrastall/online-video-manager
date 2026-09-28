using OnlineVideoManager.Core.Downloads;
using OnlineVideoManager.Core.Settings;

namespace OnlineVideoManager.Tests;

public class DownloadParsingTests
{
    [Fact]
    public void Parses_progress_with_missing_fields()
    {
        var update = Assert.Single(YtDlpOutputParser.Parse("@@P 1024|NA|4096|512.5|NA", false));
        var p = Assert.IsType<JobUpdate.Progress>(update);
        Assert.Equal(1024, p.Downloaded);
        Assert.Equal(4096, p.Total);
        Assert.Equal(512.5, p.Speed);
        Assert.Null(p.Eta);
    }

    [Fact]
    public void Parses_formats_items_stages_and_files()
    {
        Assert.Equal(2, Assert.IsType<JobUpdate.Streams>(YtDlpOutputParser.Parse("[info] abc: Downloading 1 format(s): 395+251", false)[0]).Count);
        Assert.Equal(new JobUpdate.PlaylistItem(3, 10), YtDlpOutputParser.Parse("[download] Downloading item 3 of 10", false)[0]);
        Assert.Equal(new JobUpdate.Stage("Merger"), YtDlpOutputParser.Parse("[Merger] Merging formats into \"x.mkv\"", false)[0]);
        Assert.IsType<JobUpdate.Destination>(YtDlpOutputParser.Parse("[download] Destination: x.f399.mp4", false)[0]);
        Assert.Equal(new JobUpdate.FileSaved(@"C:\Videos\x.mkv"), YtDlpOutputParser.Parse(@"@@F C:\Videos\x.mkv", false)[0]);
        Assert.Equal(new JobUpdate.Title("Some – title"), YtDlpOutputParser.Parse("@@T Some – title", false)[0]);
    }

    [Fact]
    public void Errors_flagged_only_on_stderr()
    {
        Assert.True(Assert.IsType<JobUpdate.Log>(YtDlpOutputParser.Parse("ERROR: [youtube] x: Video unavailable", true)[^1]).IsError);
        Assert.False(Assert.IsType<JobUpdate.Log>(YtDlpOutputParser.Parse("ERROR: looks like one", false)[^1]).IsError);
    }

    [Fact]
    public void Command_line_split_honours_quotes()
    {
        Assert.Equal(["--foo", "a b", "c", "", "d"], CommandLine.Split("--foo \"a b\" c  \"\" d"));
        Assert.Empty(CommandLine.Split("   "));
    }

    [Fact]
    public void Arguments_for_video_and_audio()
    {
        var settings = new AppSettings { OutputDirectory = "/out", MaxHeight = 1080, ExtraArguments = "--limit-rate 1M" };
        var video = YtDlpArguments.Build(settings, "/nonexistent-tools", DownloadMode.Video, "-abc");

        Assert.Contains("bv*+ba/b", video);
        Assert.Contains("res:1080", video);
        Assert.Contains("--no-playlist", video);
        Assert.DoesNotContain("--ffmpeg-location", video);
        Assert.Equal(["--limit-rate", "1M", "--", "-abc"], video[^4..]);

        var audio = YtDlpArguments.Build(settings, "/nonexistent-tools", DownloadMode.Audio, "https://x.com/a");
        Assert.Contains("-x", audio);
        Assert.DoesNotContain("--merge-output-format", audio);
    }
}
