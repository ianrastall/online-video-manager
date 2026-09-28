using System.Diagnostics;
using System.Text;

namespace OnlineVideoManager.Core.Downloads;

public sealed class YtDlpRunner : IDownloadRunner
{
    public async Task<int> RunAsync(DownloadRequest request, Action<JobUpdate> onUpdate, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (request.OutputDirectory is { } output) DiskSpace.Ensure(output, request.MinimumFreeBytes);

        var psi = new ProcessStartInfo(request.YtDlpPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = request.ToolsDirectory,
        };
        foreach (var arg in request.Arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        // yt-dlp finds ffmpeg/deno via its own options, but keeping the tools folder first on PATH
        // covers anything it launches by name. Force UTF-8 so titles in any script survive.
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        psi.Environment["PATH"] = request.ToolsDirectory + System.IO.Path.PathSeparator + path;
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONUTF8"] = "1";

        using var process = new Process { StartInfo = psi };
        process.Start();
        process.StandardInput.Close();

        using var monitorCts = new CancellationTokenSource();
        var monitor = MonitorDiskAsync(process, request, monitorCts.Token);

        // The PyInstaller build of yt-dlp runs Python in a child process, and ffmpeg runs below that,
        // so the whole tree must be killed or orphans keep downloading.
        using var registration = ct.Register(() =>
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
        });

        var stdout = PumpAsync(process.StandardOutput, fromStdErr: false, onUpdate);
        var stderr = PumpAsync(process.StandardError, fromStdErr: true, onUpdate);
        try
        {
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally { await monitorCts.CancelAsync().ConfigureAwait(false); }
        await monitor.ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();
        return process.ExitCode;
    }

    private static async Task MonitorDiskAsync(Process process, DownloadRequest request, CancellationToken ct)
    {
        if (request.OutputDirectory is null) return;
        try
        {
            while (!process.HasExited)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                DiskSpace.Ensure(request.OutputDirectory, request.MinimumFreeBytes);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch
        {
            try { process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            throw;
        }
    }

    private static async Task PumpAsync(StreamReader reader, bool fromStdErr, Action<JobUpdate> onUpdate)
    {
        // ReadLineAsync splits on \r as well as \n, so carriage-return progress redraws become lines.
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            line = line.TrimEnd();
            if (line.Length == 0)
            {
                continue;
            }

            foreach (var update in YtDlpOutputParser.Parse(line, fromStdErr))
            {
                onUpdate(update);
            }
        }
    }
}
