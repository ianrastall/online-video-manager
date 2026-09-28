using OnlineVideoManager.Core.Tools;

namespace OnlineVideoManager.Tests;

public class ToolTests
{
    [Fact]
    public void Checksum_from_sha256sum_list()
    {
        var text = "aaaa  other\n984c4d256c07c991919a2aced0856dfdf08c8423c23c2346a98a828480ab8e7d  ffmpeg-N-1-win64-gpl-shared.zip\n"
                 + "ef8310a639c2577d9f1a04c0b112c3659d3f3547a2582d5f519b5cd0c8487dcf  ffmpeg-n8.zip\n";

        Assert.Equal("984c4d256c07c991919a2aced0856dfdf08c8423c23c2346a98a828480ab8e7d", ChecksumParser.Find(text, "ffmpeg-N-1-win64-gpl-shared.zip"));
        Assert.Null(ChecksumParser.Find(text, "missing.zip"));
    }

    [Fact]
    public void Checksum_with_binary_marker()
    {
        var text = "A0C3101B4158D1DFB7D6A78A7BF0F3DE80C96BB423C152BEEC8BEB22786F2238 *yt-dlp.exe\n"
                 + "1111111111111111111111111111111111111111111111111111111111111111 *yt-dlp_linux\n";
        Assert.Equal("a0c3101b4158d1dfb7d6a78a7bf0f3de80c96bb423c152beec8beb22786f2238", ChecksumParser.Find(text, "yt-dlp.exe"));
    }

    [Fact]
    public void Checksum_from_powershell_output()
    {
        var text = "Algorithm : SHA256\nHash      : A0C3101B4158D1DFB7D6A78A7BF0F3DE80C96BB423C152BEEC8BEB22786F2238\n"
                 + "Path      : C:\\a\\deno\\target\\release\\deno-x86_64-pc-windows-msvc.zip\n";
        Assert.Equal("a0c3101b4158d1dfb7d6a78a7bf0f3de80c96bb423c152beec8beb22786f2238", ChecksumParser.Find(text, "deno-x86_64-pc-windows-msvc.zip"));
    }

    [Fact]
    public void Version_parsing()
    {
        Assert.Equal("N-125752-g2f209337fc", ToolManager.NormalizeFfmpegVersion("N-125752-g2f209337fc-20260724"));
        Assert.Equal("n8.1.3", ToolManager.NormalizeFfmpegVersion("n8.1.3"));
        Assert.Equal("N-125752-g2f209337fc", ToolManager.ParseFfmpegVersion("ffmpeg version N-125752-g2f209337fc-20260724 Copyright (c) 2000-2026\nbuilt with gcc"));
        Assert.Equal("2.5.1", ToolManager.ParseDenoVersion("deno 2.5.1 (stable, release, x86_64-pc-windows-msvc)\nv8 14.0\ntypescript 5.9"));
        Assert.Null(ToolManager.ParseDenoVersion("garbage"));
    }

    [Fact]
    public void Replace_file_swaps_and_removes_old_copy()
    {
        var dir = Directory.CreateTempSubdirectory("ovm-test-").FullName;
        try
        {
            var target = Path.Combine(dir, "tool.exe");
            var source = Path.Combine(dir, "new.exe");
            File.WriteAllText(target, "old");
            File.WriteAllText(source, "new");

            ToolManager.ReplaceFile(source, target);

            Assert.Equal("new", File.ReadAllText(target));
            Assert.False(File.Exists(source));
            Assert.False(File.Exists(target + ".old"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Zip_parent_folder_name()
    {
        Assert.Equal("bin", ZipExtraction.ParentFolderName("ffmpeg-N-1-win64-gpl-shared/bin/ffmpeg.exe"));
        Assert.Null(ZipExtraction.ParentFolderName("deno.exe"));
    }
}
