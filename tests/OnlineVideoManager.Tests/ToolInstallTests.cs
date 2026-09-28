using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using OnlineVideoManager.Core.Settings;
using OnlineVideoManager.Core.Tools;

namespace OnlineVideoManager.Tests;

/// <summary>Drives the real ToolManager against canned HTTP responses.</summary>
public sealed class ToolInstallTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ovm-tools-").FullName;
    private readonly FakeHttp _http = new();
    private readonly ToolManager _manager;

    public ToolInstallTests() => _manager = new ToolManager(new HttpClient(_http));

    public void Dispose()
    {
        _manager.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task Installs_ffmpeg_from_bin_folder_and_prunes_stale_libraries()
    {
        var x = ToolIdExtensions.ExecutableSuffix;
        var zip = Zip(
            ($"ffmpeg-N-2-g2/bin/ffmpeg{x}", "new ffmpeg"),
            ($"ffmpeg-N-2-g2/bin/ffprobe{x}", "new ffprobe"),
            ("ffmpeg-N-2-g2/bin/avcodec-62.dll", "codec"),
            ("ffmpeg-N-2-g2/doc/readme.txt", "docs"));
        File.WriteAllText(Path.Combine(_dir, $"ffmpeg{x}"), "old ffmpeg");
        File.WriteAllText(Path.Combine(_dir, "avcodec-61.dll"), "old codec");
        File.WriteAllText(Path.Combine(_dir, "unrelated.txt"), "keep me");
        var release = Serve("ffmpeg-N-2-g2-win64-gpl-shared.zip", zip, "checksums.sha256", $"{Sha(zip)}  ffmpeg-N-2-g2-win64-gpl-shared.zip\n{new string('0', 64)}  other.zip");

        await _manager.InstallAsync(_dir, ToolId.Ffmpeg, release with { Version = "N-2-g2" }, null, TestContext.Current.CancellationToken);

        Assert.Equal("new ffmpeg", File.ReadAllText(Path.Combine(_dir, $"ffmpeg{x}")));
        Assert.True(File.Exists(ToolIdExtensions.FfprobePath(_dir)));
        Assert.True(File.Exists(Path.Combine(_dir, "avcodec-62.dll")));
        Assert.False(File.Exists(Path.Combine(_dir, "avcodec-61.dll")));
        Assert.False(File.Exists(Path.Combine(_dir, "readme.txt")));
        Assert.True(File.Exists(Path.Combine(_dir, "unrelated.txt")));
        Assert.False(Directory.Exists(Path.Combine(_dir, ".staging")));
        var manifest = ToolManifest.Load(_dir);
        Assert.Equal("N-2-g2", manifest.Versions["ffmpeg"]);
        Assert.Contains("avcodec-62.dll", manifest.FfmpegFiles);
    }

    [Fact]
    public async Task Refuses_a_download_whose_checksum_does_not_match()
    {
        var exe = ToolId.YtDlp.ExecutablePath(_dir);
        File.WriteAllText(exe, "working old version");
        var release = Serve("yt-dlp.exe", Encoding.UTF8.GetBytes("tampered"), "SHA2-256SUMS", $"{new string('a', 64)}  yt-dlp.exe");

        await Assert.ThrowsAsync<InvalidDataException>(() => _manager.InstallAsync(_dir, ToolId.YtDlp, release, null, TestContext.Current.CancellationToken));

        Assert.Equal("working old version", File.ReadAllText(exe));
    }

    [Fact]
    public async Task Installs_single_file_yt_dlp_and_deno_from_zip()
    {
        var bytes = Encoding.UTF8.GetBytes("yt-dlp binary");
        await _manager.InstallAsync(_dir, ToolId.YtDlp, Serve("yt-dlp.exe", bytes, "SHA2-256SUMS", $"{Sha(bytes)} *yt-dlp.exe"), null, TestContext.Current.CancellationToken);
        Assert.Equal("yt-dlp binary", File.ReadAllText(ToolId.YtDlp.ExecutablePath(_dir)));

        var zip = Zip((ToolId.Deno.ExecutableName(), "deno binary"));
        var name = "deno-x86_64-pc-windows-msvc.zip";
        await _manager.InstallAsync(_dir, ToolId.Deno, Serve(name, zip, name + ".sha256sum", $"Algorithm : SHA256\nHash      : {Sha(zip).ToUpperInvariant()}\nPath      : C:\\build\\{name}"), null, TestContext.Current.CancellationToken);
        Assert.Equal("deno binary", File.ReadAllText(ToolId.Deno.ExecutablePath(_dir)));
    }

    [Fact]
    public async Task Reads_latest_yt_dlp_release_from_github()
    {
        var asset = OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp_linux";
        _http.Responses["https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest"] = Encoding.UTF8.GetBytes($$"""
            { "tag_name": "2026.09.01", "assets": [
              { "name": "yt-dlp.tar.gz", "browser_download_url": "https://dl/tar", "size": 1 },
              { "name": "{{asset}}", "browser_download_url": "https://dl/bin", "size": 1234 },
              { "name": "SHA2-256SUMS", "browser_download_url": "https://dl/sums", "size": 10 } ] }
            """);

        var release = await _manager.GetLatestAsync(ToolId.YtDlp, UpdateChannel.Stable, TestContext.Current.CancellationToken);

        Assert.Equal(new ToolRelease("2026.09.01", asset, "https://dl/bin", "https://dl/sums", 1234), release);
    }

    private ToolRelease Serve(string assetName, byte[] asset, string checksumName, string checksumText)
    {
        _http.Responses["https://dl.example/" + assetName] = asset;
        _http.Responses["https://dl.example/" + checksumName] = Encoding.UTF8.GetBytes(checksumText);
        return new ToolRelease("1.0", assetName, "https://dl.example/" + assetName, "https://dl.example/" + checksumName, asset.Length);
    }

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static byte[] Zip(params (string Path, string Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open());
                writer.Write(content);
            }
        }

        return ms.ToArray();
    }

    private sealed class FakeHttp : HttpMessageHandler
    {
        public Dictionary<string, byte[]> Responses { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Responses.TryGetValue(request.RequestUri!.ToString(), out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
