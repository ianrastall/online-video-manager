using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using OnlineVideoManager.Contracts;
using OnlineVideoManager.Interop;

var root = Path.GetFullPath(args[0]);
NativeLibrary.SetDllImportResolver(typeof(NativeEngine).Assembly, (name, _, _) =>
    name == "ovm_core" ? NativeLibrary.Load(Path.Combine(root, ".build", "native", "ovm_core.dll")) : 0);
using var smokeDirectory = new SmokeDirectory(Path.Combine(root, ".build", "smoke"));
var work = smokeDirectory.Path;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    Console.WriteLine("PASS: " + description);
}
using var engine = new NativeEngine(work);
var settings = engine.Execute(new("snapshot")).State!.Settings;
settings.CheckForToolUpdatesOnStartup = false;
settings.InstallToolUpdatesAutomatically = false;
settings.WatchClipboard = false;
settings.ToolsDirectory = Path.Combine(root, ".build", "smoke", "tools");
settings.OutputDirectory = work;
settings.EmbedThumbnail = false;
settings.Container = VideoContainer.Mp4;
settings.AudioFormat = AudioFormat.Flac;
engine.Execute(new("settings.set", Settings: settings));
try { engine.Execute(new("invalid")); throw new Exception("Missing native error"); }
catch (InvalidOperationException ex) { Check(ex.Message.Contains("Unknown engine operation"), "Native errors returned safely"); }
var collected = engine.Execute(new("clipboard.collect", Text: "https://vimeo.com/123")).State!;
Check(collected.Items.Length == 0 && collected.Inbox.Length == 1 && !collected.Running, "Clipboard collection never starts downloads");
if (!args.Contains("--live")) return;

engine.Execute(new("tools.update"));
var deadline = DateTime.UtcNow.AddMinutes(15);
var lastActivity = "";
EngineSnapshot state;
do
{
    await Task.Delay(250);
    state = engine.Execute(new("snapshot")).State!;
    var activity = string.Join("; ", state.Tools.Where(t => t.Working).Select(t => t.Id + ": " + t.Activity));
    if (activity != lastActivity) { Console.WriteLine(activity); lastActivity = activity; }
    if (DateTime.UtcNow > deadline) throw new TimeoutException("Native tool installation timed out.");
} while (state.ToolsBusy);
foreach (var tool in state.Tools)
    Check(tool.Installed.Length > 0 && tool.Installed != "unknown" && tool.Error.Length == 0, $"Native checksum-verified {tool.Id} install: {tool.Installed} {tool.Error}");
Check(File.Exists(Path.Combine(settings.ToolsDirectory, "ffprobe.exe")), "ffprobe included");

var fixture = Path.Combine(work, "fixture.mp4");
var psi = new ProcessStartInfo(Path.Combine(settings.ToolsDirectory, "ffmpeg.exe")) { UseShellExecute = false, CreateNoWindow = true };
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
        settings.OutputTemplate = "download-" + mode + ".%(ext)s";
        engine.Execute(new("settings.set", Settings: settings));
        var added = engine.Execute(new("queue.add", Text: $"http://127.0.0.1:{port}/fixture.mp4", Mode: mode)).State!.Items.Last();
        engine.Execute(new("queue.start"));
        deadline = DateTime.UtcNow.AddMinutes(2);
        DownloadSnapshot item;
        bool progress = false;
        do
        {
            await Task.Delay(50);
            item = engine.Execute(new("snapshot")).State!.Items.Single(i => i.Id == added.Id);
            progress |= item.Downloaded > 0;
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Native media download timed out.");
        } while (item.Status is DownloadStatus.Running or DownloadStatus.Queued);
        Check(item.Status == DownloadStatus.Completed, $"Native {mode} queue execution: {item.Error} " + string.Join("; ", item.Logs));
        Check(File.Exists(item.FilePath), $"Real {mode} output exists");
        Check(Path.GetExtension(item.FilePath) == (mode == DownloadMode.Video ? ".mp4" : ".flac"), $"Requested {mode} format delivered");
        Check(progress, $"Native {mode} progress crossed the ABI");
    }
}
finally { serverStop.Cancel(); listener.Stop(); await server; }

// Dispose after the engine so no native worker still holds the test files.
sealed class SmokeDirectory : IDisposable
{
    public SmokeDirectory(string parent)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetFullPath(parent), "native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }
    public string Path { get; }
    public void Dispose() => Directory.Delete(Path, recursive: true);
}
