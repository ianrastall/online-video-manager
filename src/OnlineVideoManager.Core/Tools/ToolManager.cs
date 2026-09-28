using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using OnlineVideoManager.Core.Settings;

namespace OnlineVideoManager.Core.Tools;

/// <summary>
/// Finds, checks and installs yt-dlp, deno and ffmpeg from their official GitHub releases.
/// Every download is verified against the SHA-256 the release publishes before it is installed.
/// </summary>
public sealed class ToolManager : IToolManager, IDisposable
{
    private const string StagingFolder = ".staging";

    /// <summary>DLL name prefixes in BtbN's shared FFmpeg builds; used to prune libraries whose
    /// ABI number in the file name changed between versions.</summary>
    private static readonly string[] FfmpegLibraryPrefixes =
        ["avcodec-", "avdevice-", "avfilter-", "avformat-", "avutil-", "postproc-", "swresample-", "swscale-"];

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public ToolManager()
        : this(CreateHttpClient(), ownsHttp: true)
    {
    }

    public ToolManager(HttpClient http, bool ownsHttp = false)
    {
        _http = http;
        _ownsHttp = ownsHttp;
    }

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OnlineVideoManager", version));
        return http;
    }

    // ----- local detection -------------------------------------------------

    public async Task<string?> GetLocalVersionAsync(string directory, ToolId id, CancellationToken ct = default)
    {
        var exe = id.ExecutablePath(directory);
        if (!File.Exists(exe) || (id == ToolId.Ffmpeg && !File.Exists(ToolIdExtensions.FfprobePath(directory))))
        {
            return null;
        }

        var timeout = TimeSpan.FromSeconds(20);
        string? found = id switch
        {
            ToolId.YtDlp => (await ProcessCapture.RunAsync(exe, ["--version"], timeout, ct).ConfigureAwait(false))?.Trim(),
            ToolId.Deno => ParseDenoVersion(await ProcessCapture.RunAsync(exe, ["--version"], timeout, ct).ConfigureAwait(false)),
            ToolId.Ffmpeg => ParseFfmpegVersion(await ProcessCapture.RunAsync(exe, ["-hide_banner", "-version"], timeout, ct).ConfigureAwait(false)),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(found) ? "unknown" : found;
    }

    /// <summary><c>deno 2.5.1 (stable, release, x86_64-pc-windows-msvc)</c> → <c>2.5.1</c>.</summary>
    internal static string? ParseDenoVersion(string? output)
    {
        var first = output?.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var parts = first?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts is { Length: >= 2 } && parts[0] == "deno" ? parts[1] : null;
    }

    /// <summary><c>ffmpeg version N-125752-g2f209337fc-20260724 Copyright…</c> → <c>N-125752-g2f209337fc</c>.</summary>
    internal static string? ParseFfmpegVersion(string? output)
    {
        const string prefix = "ffmpeg version ";
        var first = output?.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        if (first is null || !first.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var version = first[prefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return version is null ? null : NormalizeFfmpegVersion(version);
    }

    /// <summary>Master builds report <c>N-rev-ghash-date</c>; release asset names carry only
    /// <c>N-rev-ghash</c>.</summary>
    internal static string NormalizeFfmpegVersion(string version) =>
        version.StartsWith("N-", StringComparison.Ordinal)
            ? string.Join('-', version.Split('-').Take(3))
            : version;

    // ----- upstream releases ------------------------------------------------

    private static (string YtDlpAsset, string DenoTriple, string FfmpegSuffix) Platform
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                // x64 builds; they also run on Windows on Arm through emulation.
                return ("yt-dlp.exe", "x86_64-pc-windows-msvc", "-win64-gpl-shared.zip");
            }

            if (OperatingSystem.IsLinux() && System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.X64)
            {
                // FFmpeg for Linux ships as .tar.xz, which this app does not unpack.
                return ("yt-dlp_linux", "x86_64-unknown-linux-gnu", "");
            }

            return ("", "", "");
        }
    }

    public async Task<ToolRelease> GetLatestAsync(ToolId id, UpdateChannel channel, CancellationToken ct = default)
    {
        var platform = Platform;
        switch (id)
        {
            case ToolId.YtDlp:
            {
                Require(platform.YtDlpAsset, "yt-dlp");
                var repo = channel == UpdateChannel.Nightly ? "yt-dlp/yt-dlp-nightly-builds" : "yt-dlp/yt-dlp";
                using var doc = await GitHubAsync($"repos/{repo}/releases/latest", ct).ConfigureAwait(false);
                var rel = doc.RootElement;
                var version = rel.GetProperty("tag_name").GetString() ?? throw new InvalidDataException("Release has no tag.");
                var asset = FindAsset(rel, n => n == platform.YtDlpAsset)
                    ?? throw new InvalidDataException($"Release {version} has no {platform.YtDlpAsset}.");
                return ReleaseFrom(rel, version, asset, "SHA2-256SUMS");
            }

            case ToolId.Deno:
            {
                Require(platform.DenoTriple, "deno");
                using var doc = await GitHubAsync("repos/denoland/deno/releases/latest", ct).ConfigureAwait(false);
                var rel = doc.RootElement;
                var tag = rel.GetProperty("tag_name").GetString() ?? throw new InvalidDataException("Release has no tag.");
                var name = $"deno-{platform.DenoTriple}.zip";
                var asset = FindAsset(rel, n => n == name) ?? throw new InvalidDataException($"Release {tag} has no {name}.");
                return ReleaseFrom(rel, tag.TrimStart('v'), asset, name + ".sha256sum");
            }

            case ToolId.Ffmpeg:
            {
                if (platform.FfmpegSuffix.Length == 0)
                {
                    throw new PlatformNotSupportedException("Automatic ffmpeg installs are only supported on Windows.");
                }

                // BtbN's dated "autobuild-" releases carry the git revision in the asset name, which is
                // what `ffmpeg -version` reports for master builds, so versions can be compared.
                using var doc = await GitHubAsync("repos/BtbN/FFmpeg-Builds/releases?per_page=10", ct).ConfigureAwait(false);
                foreach (var rel in doc.RootElement.EnumerateArray())
                {
                    if (!(rel.GetProperty("tag_name").GetString() ?? "").StartsWith("autobuild-", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var asset = FindAsset(rel, n => n.StartsWith("ffmpeg-N-", StringComparison.Ordinal) && n.EndsWith(platform.FfmpegSuffix, StringComparison.Ordinal));
                    if (asset is { } a)
                    {
                        var name = a.GetProperty("name").GetString()!;
                        var version = name["ffmpeg-".Length..^platform.FfmpegSuffix.Length];
                        return ReleaseFrom(rel, version, a, "checksums.sha256");
                    }
                }

                throw new InvalidDataException("No FFmpeg master build found in recent releases.");
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(id));
        }
    }

    private static void Require(string value, string tool)
    {
        if (value.Length == 0)
        {
            throw new PlatformNotSupportedException($"No {tool} build is available for this platform.");
        }
    }

    private async Task<JsonDocument> GitHubAsync(string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/" + path);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? Environment.GetEnvironmentVariable("GH_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var hint = response.StatusCode is System.Net.HttpStatusCode.Forbidden or (System.Net.HttpStatusCode)429
                ? " (GitHub rate limit; try again later)"
                : "";
            throw new HttpRequestException($"GitHub returned {(int)response.StatusCode} for {path}{hint}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
    }

    private static JsonElement? FindAsset(JsonElement release, Func<string, bool> predicate)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.TryGetProperty("name", out var name) && name.GetString() is { } n && predicate(n))
            {
                return asset;
            }
        }

        return null;
    }

    private static ToolRelease ReleaseFrom(JsonElement release, string version, JsonElement asset, string checksumAssetName)
    {
        var checksum = FindAsset(release, n => n == checksumAssetName);
        return new ToolRelease(
            version,
            asset.GetProperty("name").GetString() ?? "",
            asset.GetProperty("browser_download_url").GetString() ?? throw new InvalidDataException("Asset has no download URL."),
            checksum?.GetProperty("browser_download_url").GetString(),
            asset.TryGetProperty("size", out var size) && size.TryGetInt64(out var s) ? s : null);
    }

    // ----- install -----------------------------------------------------------

    public void Cleanup(string directory)
    {
        try
        {
            var staging = Path.Combine(directory, StagingFolder);
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }

            if (Directory.Exists(directory))
            {
                foreach (var old in Directory.EnumerateFiles(directory, "*.old"))
                {
                    TryDelete(old);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public async Task InstallAsync(string directory, ToolId id, ToolRelease release, IProgress<ToolInstallProgress>? progress, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(20));
        ct = timeout.Token;
        Directory.CreateDirectory(directory);
        Downloads.DiskSpace.Ensure(directory, Math.Max(Downloads.DiskSpace.DefaultReserve, (release.Size ?? 0) * 4));
        var staging = Path.Combine(directory, StagingFolder);
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }

        Directory.CreateDirectory(staging);
        try
        {
            await InstallStagedAsync(directory, staging, id, release, progress, ct).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                Directory.Delete(staging, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private async Task InstallStagedAsync(string directory, string staging, ToolId id, ToolRelease release, IProgress<ToolInstallProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new ToolInstallProgress($"Downloading {release.AssetName}", 0, release.Size));
        var archive = Path.Combine(staging, release.AssetName);
        var actual = await DownloadAsync(release, archive, progress, ct).ConfigureAwait(false);

        if (release.ChecksumUrl is null)
        {
            throw new InvalidDataException($"The release publishes no checksum for {release.AssetName}; refusing to install it.");
        }

        progress?.Report(new ToolInstallProgress("Verifying checksum"));
        var sums = await _http.GetStringAsync(release.ChecksumUrl, ct).ConfigureAwait(false);
        var expected = ChecksumParser.Find(sums, release.AssetName)
            ?? throw new InvalidDataException($"No checksum is listed for {release.AssetName}.");
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Checksum mismatch for {release.AssetName} (expected {expected}, got {actual}).");
        }

        progress?.Report(new ToolInstallProgress("Unpacking"));
        var unpacked = Path.Combine(staging, "out");
        Directory.CreateDirectory(unpacked);
        var exeName = id.ExecutableName();
        List<string> files = id switch
        {
            ToolId.YtDlp => MoveSingle(archive, Path.Combine(unpacked, exeName)),
            ToolId.Deno => ZipExtraction.Extract(archive, unpacked, entry => Path.GetFileName(entry) == exeName),
            ToolId.Ffmpeg => ZipExtraction.Extract(archive, unpacked, entry => ZipExtraction.ParentFolderName(entry) == "bin"),
            _ => throw new ArgumentOutOfRangeException(nameof(id)),
        };
        if (!files.Contains(exeName))
        {
            throw new InvalidDataException($"{exeName} was not found in {release.AssetName}.");
        }
        if (id == ToolId.Ffmpeg && !files.Contains(Path.GetFileName(ToolIdExtensions.FfprobePath(directory))))
            throw new InvalidDataException("The FFmpeg archive does not contain ffprobe; refusing an incomplete install.");

        progress?.Report(new ToolInstallProgress("Installing"));
        foreach (var name in files)
        {
            var source = Path.Combine(unpacked, name);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(source, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            ReplaceFile(source, Path.Combine(directory, name));
        }

        var manifest = ToolManifest.Load(directory);
        if (id == ToolId.Ffmpeg)
        {
            PruneFfmpegLibraries(directory, manifest.FfmpegFiles, files);
            manifest.FfmpegFiles = files;
        }

        manifest.Versions[id.Name()] = release.Version;
        manifest.Save(directory);
    }

    private static List<string> MoveSingle(string source, string destination)
    {
        File.Move(source, destination);
        return [Path.GetFileName(destination)];
    }

    /// <summary>Stream the asset to disk and return its SHA-256 as lowercase hex.</summary>
    private async Task<string> DownloadAsync(ToolRelease release, string destination, IProgress<ToolInstallProgress>? progress, CancellationToken ct)
    {
        using var response = await _http.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? release.Size;
        var stage = $"Downloading {release.AssetName}";

        await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1 << 18];
        long done = 0;
        var lastReport = Environment.TickCount64;
        int read;
        while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            done += read;
            if (Environment.TickCount64 - lastReport >= 100)
            {
                lastReport = Environment.TickCount64;
                progress?.Report(new ToolInstallProgress(stage, done, total));
            }
        }

        progress?.Report(new ToolInstallProgress(stage, done, total));
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>Swap <paramref name="source"/> into place. The current file is renamed aside first,
    /// which Windows allows even while the program is running; the renamed copy is deleted now
    /// or, if still in use, by <see cref="Cleanup"/> on a later start.</summary>
    internal static void ReplaceFile(string source, string destination)
    {
        string? aside = null;
        if (File.Exists(destination))
        {
            aside = destination + ".old";
            if (File.Exists(aside) && !TryDelete(aside))
            {
                aside = $"{destination}.{DateTime.UtcNow.Ticks}.old";
            }

            File.Move(destination, aside);
        }

        try
        {
            File.Move(source, destination);
        }
        catch
        {
            if (aside is not null)
            {
                File.Move(aside, destination);
            }

            throw;
        }

        if (aside is not null)
        {
            TryDelete(aside);
        }
    }

    private static void PruneFfmpegLibraries(string directory, IReadOnlyCollection<string> previous, IReadOnlyCollection<string> current)
    {
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            var name = Path.GetFileName(path);
            if (current.Contains(name))
            {
                continue;
            }

            var knownLibrary = FfmpegLibraryPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                && name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
            if (previous.Contains(name) || knownLibrary)
            {
                TryDelete(path);
            }
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
