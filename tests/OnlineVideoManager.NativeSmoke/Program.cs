using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using OnlineVideoManager.Core.Downloads;
using OnlineVideoManager.Core.Interop;
using OnlineVideoManager.Core.Settings;
using OnlineVideoManager.Core.Tools;
using OnlineVideoManager.Interop;

var root = Path.GetFullPath(args[0]);
NativeLibrary.SetDllImportResolver(typeof(NativeCore).Assembly, (name, _, _) =>
    name == "ovm_core" ? NativeLibrary.Load(Path.Combine(root, "artifacts", "native", "ovm_core.dll")) : 0);
var work = Path.Combine(root, "artifacts", "smoke");
Directory.CreateDirectory(work);
void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    Console.WriteLine("PASS: " + description);
}

var missing = await NativeCore.CallAsync(new("version", Path.Combine(work, "missing")));
Check(missing.Version is null, "Missing-tool lookup through native ABI");
try { await NativeCore.CallAsync(new("invalid")); throw new Exception("Missing error"); }
catch (InvalidOperationException ex) { Check(ex.Message.Contains("Unknown operation"), "Native errors returned safely"); }

var script = Path.Combine(work, "fake-downloader.ps1");
await File.WriteAllTextAsync(script, "[Console]::OutputEncoding=[Text.UTF8Encoding]::new(); Write-Output '@@T Native ABI smoke'; Write-Output '@@P 50|100|NA|25|2'; Write-Output '@@F C:\\test.mp4'", new UTF8Encoding(true));
var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
var runner = new NativeDownloadRunner();
var updates = new ConcurrentQueue<JobUpdate>();
var request = new DownloadRequest(powershell, work, ["-NoProfile", "-File", script], work);
Check(await runner.RunAsync(request, updates.Enqueue, default) == 0, "Process execution through native engine");
Check(updates.Any(e => e is JobUpdate.Progress { Downloaded: 50 }) && updates.Any(e => e is JobUpdate.Title), "Progress and title callbacks cross C ABI");

await File.WriteAllTextAsync(script, "Start-Sleep -Seconds 60");
using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500)))
{
    var timer = Stopwatch.StartNew();
    try { await runner.RunAsync(request, _ => { }, cancellation.Token); throw new Exception("Did not cancel"); }
    catch (OperationCanceledException) { Check(timer.Elapsed < TimeSpan.FromSeconds(10), "Cancellation terminates the process"); }
}
try
{
    await runner.RunAsync(request with { MinimumFreeBytes = long.MaxValue }, _ => { }, default);
    throw new Exception("Did not block low disk");
}
catch (InvalidOperationException ex) { Check(ex.Message.Contains("Low disk space"), "Native engine refuses insufficient disk space"); }

if (!args.Contains("--live")) return;
var toolDirectory = Path.Combine(work, "tools");
var manager = new NativeToolManager();
foreach (var id in ToolIdExtensions.All)
{
    var release = await manager.GetLatestAsync(id, UpdateChannel.Stable);
    if (await manager.GetLocalVersionAsync(toolDirectory, id) != release.Version)
        await manager.InstallAsync(toolDirectory, id, release, null);
    Check(await manager.GetLocalVersionAsync(toolDirectory, id) is not (null or "unknown"), $"Live {id} checksum-verified install and executable version");
}
Check(File.Exists(ToolIdExtensions.FfprobePath(toolDirectory)), "FFprobe installed");
var fixture = Path.Combine(work, "fixture.mp4");
var psi = new ProcessStartInfo(ToolId.Ffmpeg.ExecutablePath(toolDirectory)) { UseShellExecute = false, CreateNoWindow = true };
foreach (var argument in new[] { "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "color=c=blue:s=160x90:d=1", "-f", "lavfi", "-i", "sine=frequency=440:duration=1", "-c:v", "libx264", "-c:a", "aac", "-shortest", fixture }) psi.ArgumentList.Add(argument);
using (var process = Process.Start(psi)!) { await process.WaitForExitAsync(); Check(process.ExitCode == 0, "Generated one-second media fixture"); }
var media = await File.ReadAllBytesAsync(fixture);
var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var port = ((IPEndPoint)listener.LocalEndpoint).Port;
using var serverStop = new CancellationTokenSource();
var server = Task.Run(async () =>
{
    try
    {
        while (!serverStop.IsCancellationRequested)
        {
            using var client = await listener.AcceptTcpClientAsync(serverStop.Token);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            var first = await reader.ReadLineAsync();
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
            var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: video/mp4\r\nContent-Length: {media.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header);
            if (first?.StartsWith("HEAD") != true) await stream.WriteAsync(media);
        }
    }
    catch (OperationCanceledException) { }
});
try
{
    foreach (var mode in new[] { DownloadMode.Video, DownloadMode.Audio })
    {
        var settings = new AppSettings { OutputDirectory = work, OutputTemplate = "download-" + mode + ".%(ext)s", Container = VideoContainer.Mp4, AudioFormat = AudioFormat.Flac, EmbedThumbnail = false };
        var url = $"http://127.0.0.1:{port}/fixture.mp4";
        var arguments = YtDlpArguments.Build(settings, toolDirectory, mode, url);
        var events = new ConcurrentQueue<JobUpdate>();
        var exitCode = await runner.RunAsync(new(ToolId.YtDlp.ExecutablePath(toolDirectory), toolDirectory, arguments, work), events.Enqueue, default);
        Check(exitCode == 0, $"Real yt-dlp {mode} download and FFmpeg processing: " + string.Join("; ", events.OfType<JobUpdate.Log>().Where(e => e.IsError).Select(e => e.Line)));
        Check(events.OfType<JobUpdate.FileSaved>().Any(e => File.Exists(e.Path)), $"Real {mode} output exists");
    }
}
finally { serverStop.Cancel(); listener.Stop(); await server; }
