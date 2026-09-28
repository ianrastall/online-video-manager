using Microsoft.Extensions.DependencyInjection;
using OnlineVideoManager.Interop;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using OnlineVideoManager.App.Services;
using OnlineVideoManager.Core;
using OnlineVideoManager.Core.Downloads;
using OnlineVideoManager.Core.Settings;
using OnlineVideoManager.Core.Tools;
using OnlineVideoManager.ViewModels;
using OnlineVideoManager.ViewModels.Services;

namespace OnlineVideoManager.App;

public partial class App : Application
{
    private ServiceProvider? _services;
    private MainWindow? _window;

    public App()
    {
        DebugSettings.IsXamlResourceReferenceTracingEnabled = true;
        DebugSettings.XamlResourceReferenceFailed += (_, e) => CrashLog.Write(new InvalidOperationException(e.Message));
        InitializeComponent();
        UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write(e.Exception);
            e.SetObserved();
        };
    }

    public static IServiceProvider Services => ((App)Current)._services ?? throw new InvalidOperationException("Services are not ready.");

    public static T GetService<T>()
        where T : notnull => Services.GetRequiredService<T>();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (!SingleInstance.TryClaim())
        {
            // Another copy is running: bring it forward instead of opening a second queue and watcher.
            SingleInstance.ActivateExisting(MainWindow.WindowTitle);
            Exit();
            return;
        }

        _services = ConfigureServices(DispatcherQueue.GetForCurrentThread());
        _window = _services.GetRequiredService<MainWindow>();
        _window.Closed += (_, _) => _services.Dispose();
        _window.Activate();
    }

    private static ServiceProvider ConfigureServices(DispatcherQueue dispatcherQueue)
    {
        var settingsStore = SettingsStore.Default;
        var settings = settingsStore.Load();

        var services = new ServiceCollection();

        // Core
        services.AddSingleton(new SettingsService(settings, settingsStore));
        services.AddSingleton(QueueStore.Default);
        services.AddSingleton<IDownloadRunner, NativeDownloadRunner>();
        services.AddSingleton<IToolManager>(_ => new NativeToolManager());

        // Platform services
        services.AddSingleton<IUiDispatcher>(new DispatcherQueueUiDispatcher(dispatcherQueue));
        services.AddSingleton<WindowContext>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IPickerService, PickerService>();
        services.AddSingleton<IShellService, ShellService>();

        // View models
        services.AddSingleton<NotificationViewModel>();
        services.AddSingleton<WorkCoordinator>();
        services.AddSingleton<DownloadsViewModel>();
        services.AddSingleton<ClipboardInboxViewModel>();
        services.AddSingleton<ToolsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainViewModel>();

        services.AddSingleton<MainWindow>();
        return services.BuildServiceProvider();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        CrashLog.Write(new InvalidOperationException(e.Message, e.Exception));
        if (_services?.GetService<NotificationViewModel>() is { } notifications)
        {
            // Keep the app alive; the queue and settings are saved as they change.
            e.Handled = true;
            notifications.Show($"Something went wrong: {e.Exception.Message} (details in {CrashLog.Path})", NotificationLevel.Error);
        }
    }
}

internal static class CrashLog
{
    public static string Path => System.IO.Path.Combine(AppPaths.DataDirectory, "error.log");

    public static void Write(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.AppendAllText(Path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
