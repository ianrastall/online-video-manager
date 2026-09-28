using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OnlineVideoManager.Core.Tools;
using OnlineVideoManager.ViewModels.Services;

namespace OnlineVideoManager.ViewModels;

/// <summary>
/// Keeps yt-dlp, deno and ffmpeg (with ffprobe) installed and current.
/// Runs on the UI thread; awaits resume there.
/// </summary>
public sealed partial class ToolsViewModel : ObservableObject
{
    private const int MaxLogLines = 200;

    private readonly SettingsService _settings;
    private readonly IToolManager _manager;
    private readonly IShellService _shell;
    private readonly NotificationViewModel _notifications;
    private readonly WorkCoordinator _coordinator;
    private Task _current = Task.CompletedTask;

    public ToolsViewModel(SettingsService settings, IToolManager manager, IShellService shell, NotificationViewModel notifications, WorkCoordinator coordinator)
    {
        _settings = settings;
        _manager = manager;
        _shell = shell;
        _notifications = notifications;
        _coordinator = coordinator;
        foreach (var id in ToolIdExtensions.All)
        {
            Tools.Add(new ToolItemViewModel(this, id));
        }

        ToolsDirectory = settings.ToolsDirectory;
        _settings.Changed += OnSettingsChanged;
    }

    public ObservableCollection<ToolItemViewModel> Tools { get; } = [];

    public ObservableCollection<string> LogLines { get; } = [];

    [ObservableProperty]
    public partial string ToolsDirectory { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand), nameof(UpdateAllCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    public ToolItemViewModel this[ToolId id] => Tools.First(t => t.Id == id);

    partial void OnIsBusyChanged(bool value)
    {
        foreach (var tool in Tools)
        {
            tool.RefreshCommands();
        }
    }

    private async void OnSettingsChanged(object? sender, EventArgs e)
    {
        var directory = _settings.ToolsDirectory;
        if (string.Equals(directory, ToolsDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ToolsDirectory = directory;
        try
        {
            await RunExclusiveAsync(() => CheckCoreAsync(fetchLatest: false));
        }
        catch (Exception ex)
        {
            Log($"Refresh failed: {ex.Message}");
        }
    }

    /// <summary>Launch-time routine: detect what is installed, optionally check upstream,
    /// and install missing or outdated tools when automatic installs are on.</summary>
    public async Task StartupAsync()
    {
        _manager.Cleanup(ToolsDirectory);
        var current = _settings.Current;
        await RunExclusiveAsync(async () =>
        {
            await CheckCoreAsync(fetchLatest: current.CheckForToolUpdatesOnStartup);
            var needed = Tools.Where(t => !t.IsInstalled || t.UpdateAvailable).ToList();
            if (needed.Count == 0)
            {
                return;
            }

            if (current.InstallToolUpdatesAutomatically)
            {
                _notifications.Show($"Installing {string.Join(", ", needed.Select(t => t.Name))}. Progress is shown on the Tools page.", NotificationLevel.Info);
                await InstallCoreAsync(needed);
            }
            else
            {
                var missing = Tools.Where(t => !t.IsInstalled).Select(t => t.Name).ToList();
                _notifications.Show(
                    missing.Count > 0
                        ? $"Not installed: {string.Join(", ", missing)}. Open Tools to install."
                        : $"Updates available: {string.Join(", ", needed.Select(t => t.Name))}. Open Tools to update.",
                    missing.Count > 0 ? NotificationLevel.Warning : NotificationLevel.Info);
            }
        });
    }

    /// <summary>Install whatever is missing. Returns true when yt-dlp is installed afterwards.</summary>
    public async Task<bool> InstallMissingAsync()
    {
        await RunExclusiveAsync(async () =>
        {
            await CheckCoreAsync(fetchLatest: false);
            var missing = Tools.Where(t => !t.IsInstalled).ToList();
            if (missing.Count > 0)
            {
                await InstallCoreAsync(missing);
            }
        });
        return Tools.All(t => t.IsInstalled);
    }

    private bool CanRun() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task CheckForUpdatesAsync() => RunExclusiveAsync(async () =>
    {
        await CheckCoreAsync(fetchLatest: true);
        var outdated = Tools.Where(t => t.UpdateAvailable).Select(t => t.Name).ToList();
        var failed = Tools.Where(t => t.LatestError is not null).Select(t => t.Name).ToList();
        if (failed.Count > 0)
        {
            _notifications.Show($"Could not check {string.Join(", ", failed)} for updates. See the log below.", NotificationLevel.Warning);
        }
        else if (outdated.Count > 0)
        {
            _notifications.Show($"Updates available: {string.Join(", ", outdated)}.", NotificationLevel.Info);
        }
        else if (Tools.All(t => t.IsInstalled))
        {
            _notifications.Show("All tools are up to date.", NotificationLevel.Success);
        }
    });

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task UpdateAllAsync() => RunExclusiveAsync(async () =>
    {
        if (Tools.Any(t => t.Latest is null))
        {
            await CheckCoreAsync(fetchLatest: true);
        }

        var needed = Tools.Where(t => !t.IsInstalled || t.UpdateAvailable).ToList();
        if (needed.Count == 0)
        {
            _notifications.Show("All tools are up to date.", NotificationLevel.Success);
            return;
        }

        await InstallCoreAsync(needed);
    });

    [RelayCommand]
    private void OpenToolsFolder()
    {
        try
        {
            Directory.CreateDirectory(ToolsDirectory);
            _shell.OpenFolder(ToolsDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _notifications.Show($"Could not open the tools folder: {ex.Message}", NotificationLevel.Error);
        }
    }

    internal Task InstallAsync(IReadOnlyList<ToolItemViewModel> tools) => RunExclusiveAsync(() => InstallCoreAsync(tools));

    /// <summary>Run one tool operation at a time; a request made while another runs waits for it.</summary>
    private async Task RunExclusiveAsync(Func<Task> operation)
    {
        while (!_current.IsCompleted)
        {
            try
            {
                await _current;
            }
            catch
            {
                // The earlier operation reported its own failure.
            }
        }

        var task = RunGuardedAsync(operation);
        _current = task;
        await task;
    }

    private async Task RunGuardedAsync(Func<Task> operation)
    {
        IsBusy = true;
        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            Log($"Error: {ex.Message}");
            _notifications.Show($"Tool operation failed: {ex.Message}", NotificationLevel.Error);
        }
        finally
        {
            IsBusy = false;
            StatusText = "";
        }
    }

    private async Task CheckCoreAsync(bool fetchLatest)
    {
        var directory = ToolsDirectory;
        var channel = _settings.Current.YtDlpChannel;
        StatusText = fetchLatest ? "Checking for updates" : "Looking for installed tools";

        var tasks = Tools.Select(async tool =>
        {
            tool.IsWorking = true;
            tool.ActivityText = "Checking";
            tool.IsProgressIndeterminate = true;
            try
            {
                tool.InstalledVersion = await _manager.GetLocalVersionAsync(directory, tool.Id);
                UpdateDetail(tool, directory);
                if (fetchLatest)
                {
                    try
                    {
                        tool.Latest = await _manager.GetLatestAsync(tool.Id, channel);
                        tool.LatestError = null;
                    }
                    catch (Exception ex)
                    {
                        tool.Latest = null;
                        tool.LatestError = ex.Message;
                        Log($"{tool.Name}: update check failed: {ex.Message}");
                    }
                }
            }
            finally
            {
                tool.IsWorking = false;
            }
        });
        await Task.WhenAll(tasks);
    }

    private async Task InstallCoreAsync(IReadOnlyList<ToolItemViewModel> tools)
    {
        _coordinator.SetToolsBusy(true);
        try
        {
            if (_coordinator.ActiveDownloads > 0)
            {
                StatusText = $"Waiting for {Formatting.Plural(_coordinator.ActiveDownloads, "download")} to finish before updating tools";
                Log(StatusText);
                await _coordinator.WaitForDownloadsIdleAsync();
            }

            var directory = ToolsDirectory;
            var channel = _settings.Current.YtDlpChannel;
            var failures = new List<string>();
            foreach (var tool in tools)
            {
                StatusText = $"Installing {tool.Name}";
                tool.IsWorking = true;
                tool.ActivityText = "Preparing";
                tool.IsProgressIndeterminate = true;
                try
                {
                    var release = tool.Latest ?? await _manager.GetLatestAsync(tool.Id, channel);
                    tool.Latest = release;
                    tool.LatestError = null;
                    Log($"{tool.Name}: installing {release.Version}");
                    var progress = new Progress<ToolInstallProgress>(p =>
                    {
                        tool.ActivityText = p.Fraction is { } f ? $"{p.Stage} ({f:P0})" : p.Stage;
                        tool.IsProgressIndeterminate = p.Fraction is null;
                        tool.ProgressValue = (p.Fraction ?? 0) * 100;
                    });
                    await _manager.InstallAsync(directory, tool.Id, release, progress);
                    tool.InstalledVersion = await _manager.GetLocalVersionAsync(directory, tool.Id);
                    UpdateDetail(tool, directory);
                    Log($"{tool.Name}: installed {tool.InstalledVersion}");
                }
                catch (Exception ex)
                {
                    failures.Add(tool.Name);
                    Log($"{tool.Name}: install failed: {ex.Message}");
                }
                finally
                {
                    tool.IsWorking = false;
                }
            }

            if (failures.Count > 0)
            {
                _notifications.Show($"Could not install {string.Join(", ", failures)}. See the log on the Tools page.", NotificationLevel.Error);
            }
            else
            {
                _notifications.Show($"Installed {string.Join(", ", tools.Select(t => $"{t.Name} {t.InstalledVersion}"))}.", NotificationLevel.Success);
            }
        }
        finally
        {
            _coordinator.SetToolsBusy(false);
        }
    }

    private static void UpdateDetail(ToolItemViewModel tool, string directory)
    {
        if (tool.Id == ToolId.Ffmpeg && tool.IsInstalled)
        {
            tool.Detail = File.Exists(ToolIdExtensions.FfprobePath(directory))
                ? "ffprobe installed alongside"
                : "ffprobe is missing; reinstall ffmpeg to restore it";
        }
        else
        {
            tool.Detail = "";
        }
    }

    private void Log(string line)
    {
        if (LogLines.Count >= MaxLogLines)
        {
            LogLines.RemoveAt(0);
        }

        LogLines.Add($"{DateTime.Now:HH:mm:ss}  {line}");
    }
}
