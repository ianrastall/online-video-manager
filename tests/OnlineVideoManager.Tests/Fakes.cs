using System.Collections.Concurrent;
using OnlineVideoManager.Core.Downloads;
using OnlineVideoManager.Core.Settings;
using OnlineVideoManager.Core.Tools;
using OnlineVideoManager.ViewModels;
using OnlineVideoManager.ViewModels.Services;

namespace OnlineVideoManager.Tests;

/// <summary>Collects posted actions; the test thread plays the UI thread by pumping them.</summary>
internal sealed class ManualDispatcher : IUiDispatcher
{
    private readonly BlockingCollection<Action> _queue = [];

    public void Post(Action action) => _queue.Add(action);

    public void RunUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            var remaining = deadline - Environment.TickCount64;
            if (remaining <= 0)
            {
                throw new TimeoutException("Condition not reached.");
            }

            if (_queue.TryTake(out var action, TimeSpan.FromMilliseconds(Math.Min(remaining, 50))))
            {
                action();
            }
        }
    }
}

internal sealed class FakeRunner : IDownloadRunner
{
    public sealed record Run(DownloadRequest Request, TaskCompletionSource<int> Exit, Action<JobUpdate> OnUpdate);

    private readonly ConcurrentQueue<Run> _runs = new();

    public IReadOnlyList<Run> Runs => _runs.ToList();

    public Task<int> RunAsync(DownloadRequest request, Action<JobUpdate> onUpdate, CancellationToken ct)
    {
        var exit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        ct.Register(() => exit.TrySetCanceled(ct));
        _runs.Enqueue(new Run(request, exit, onUpdate));
        return exit.Task;
    }
}

internal sealed class FakeClipboard : IClipboardService
{
    public event EventHandler? ContentChanged;

    public ClipboardSnapshot? Current { get; set; }

    public ClipboardHistoryResult History { get; set; } = new(ClipboardHistoryStatus.Success, []);

    public string? LastSet { get; private set; }

    public void Copy(string text)
    {
        Current = new ClipboardSnapshot(text, null);
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task<ClipboardSnapshot?> ReadAsync() => Task.FromResult(Current);

    public Task<ClipboardHistoryResult> ReadHistoryAsync() => Task.FromResult(History);

    public void SetText(string text) => LastSet = text;
}

internal sealed class FakePickers : IPickerService
{
    public string? Folder { get; set; }

    public string? TextFile { get; set; }

    public Task<string?> PickFolderAsync() => Task.FromResult(Folder);

    public Task<string?> PickTextFileAsync() => Task.FromResult(TextFile);
}

internal sealed class FakeShell : IShellService
{
    public List<string> Opened { get; } = [];

    public void OpenFolder(string path) => Opened.Add(path);

    public void RevealFile(string path) => Opened.Add(path);

    public void OpenFile(string path) => Opened.Add(path);
}

internal sealed class FakeToolManager : IToolManager
{
    public Dictionary<ToolId, string?> Installed { get; } = new() { [ToolId.YtDlp] = null, [ToolId.Deno] = null, [ToolId.Ffmpeg] = null };

    public Dictionary<ToolId, string> Latest { get; } = new() { [ToolId.YtDlp] = "2026.09.01", [ToolId.Deno] = "2.5.1", [ToolId.Ffmpeg] = "N-1-gabc" };

    public List<ToolId> InstallCalls { get; } = [];

    public Task<string?> GetLocalVersionAsync(string directory, ToolId id, CancellationToken ct = default) => Task.FromResult(Installed[id]);

    public Task<ToolRelease> GetLatestAsync(ToolId id, UpdateChannel channel, CancellationToken ct = default) =>
        Task.FromResult(new ToolRelease(Latest[id], id.Name() + ".zip", "https://example.invalid/" + id, "https://example.invalid/sums", 1));

    public Task InstallAsync(string directory, ToolId id, ToolRelease release, IProgress<ToolInstallProgress>? progress, CancellationToken ct = default)
    {
        InstallCalls.Add(id);
        Installed[id] = release.Version;
        return Task.CompletedTask;
    }

    public void Cleanup(string directory)
    {
    }
}

/// <summary>A wired-up set of view models over a temporary data folder.</summary>
internal sealed class TestApp : IDisposable
{
    public TestApp(bool installYtDlp = true, Action<AppSettings>? configure = null)
    {
        Root = Directory.CreateTempSubdirectory("ovm-vm-").FullName;
        ToolsDir = Path.Combine(Root, "tools");
        Directory.CreateDirectory(ToolsDir);
        if (installYtDlp)
        {
            File.WriteAllText(ToolId.YtDlp.ExecutablePath(ToolsDir), "");
            File.WriteAllText(ToolId.Deno.ExecutablePath(ToolsDir), "");
            File.WriteAllText(ToolId.Ffmpeg.ExecutablePath(ToolsDir), "");
            File.WriteAllText(ToolIdExtensions.FfprobePath(ToolsDir), "");
        }

        var settings = new AppSettings
        {
            OutputDirectory = Path.Combine(Root, "out"),
            ToolsDirectory = ToolsDir,
            Concurrency = 2,
            StartQueueAutomatically = true,
        };
        configure?.Invoke(settings);
        Settings = new SettingsService(settings, new SettingsStore(Path.Combine(Root, "settings.json")));
        Queue = new QueueStore(Path.Combine(Root, "queue.json"));
        Downloads = new DownloadsViewModel(Settings, Queue, Runner, Dispatcher, Pickers, Shell, Clipboard, Notifications, Coordinator);
        Inbox = new ClipboardInboxViewModel(Settings, Clipboard, Downloads, Notifications);
        Tools = new ToolsViewModel(Settings, ToolManager, Shell, Notifications, Coordinator);
    }

    public string Root { get; }
    public string ToolsDir { get; }
    public SettingsService Settings { get; }
    public QueueStore Queue { get; }
    public ManualDispatcher Dispatcher { get; } = new();
    public FakeRunner Runner { get; } = new();
    public FakeClipboard Clipboard { get; } = new();
    public FakePickers Pickers { get; } = new();
    public FakeShell Shell { get; } = new();
    public FakeToolManager ToolManager { get; } = new();
    public NotificationViewModel Notifications { get; } = new();
    public WorkCoordinator Coordinator { get; } = new();
    public DownloadsViewModel Downloads { get; }
    public ClipboardInboxViewModel Inbox { get; }
    public ToolsViewModel Tools { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
