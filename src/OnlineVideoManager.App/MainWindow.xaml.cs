using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OnlineVideoManager.App.Services;
using OnlineVideoManager.App.Views;
using OnlineVideoManager.ViewModels;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;

namespace OnlineVideoManager.App;

public sealed partial class MainWindow : Window
{
    public const string WindowTitle = "Online Video Manager";

    private bool _started;

    public MainWindow(MainViewModel viewModel, WindowContext windowContext)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Title = WindowTitle;

        var hwnd = WindowNative.GetWindowHandle(this);
        windowContext.Handle = hwnd;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        StyleCaptionButtons(AppWindow.TitleBar);

        var scale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;
        if (scale <= 0)
        {
            scale = 1;
        }

        AppWindow.Resize(new SizeInt32((int)(1180 * scale), (int)(860 * scale)));

        RootGrid.Loaded += OnLoaded;
        Closed += (_, _) => ViewModel.Shutdown();
    }

    public MainViewModel ViewModel { get; }

    /// <summary>The app is always dark; make the minimize/maximize/close buttons match.</summary>
    private static void StyleCaptionButtons(AppWindowTitleBar titleBar)
    {
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = Colors.White;
        titleBar.ButtonInactiveForegroundColor = Color.FromArgb(0xFF, 0x8A, 0x8A, 0x8A);
        titleBar.ButtonHoverBackgroundColor = Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF);
        titleBar.ButtonHoverForegroundColor = Colors.White;
        titleBar.ButtonPressedBackgroundColor = Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF);
        titleBar.ButtonPressedForegroundColor = Colors.White;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        Nav.SelectedItem = DownloadsItem;
        try
        {
            await ViewModel.StartAsync();
            // Opt-in build acceptance hook: exercise compiled XAML from the actual publish directory.
            if (Environment.GetEnvironmentVariable("OVM_SMOKE_TEST") == "1")
            {
                foreach (var page in new[] { typeof(DownloadsPage), typeof(ToolsPage), typeof(SettingsPage) })
                {
                    if (!ContentFrame.Navigate(page)) throw new InvalidOperationException($"Could not load {page.Name}.");
                    await Task.Delay(150);
                }
                File.WriteAllText(Path.Combine(App.GetService<SettingsService>().DataDirectory, "smoke-passed"), "All pages loaded.");
                ViewModel.Shutdown();
                Application.Current.Exit();
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            ViewModel.Notifications.Show($"Start-up failed: {ex.Message}", NotificationLevel.Error);
        }
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        var page = args.IsSettingsSelected ? typeof(SettingsPage)
            : tag == "Tools" ? typeof(ToolsPage)
            : typeof(DownloadsPage);

        if (ContentFrame.CurrentSourcePageType != page)
        {
            ContentFrame.Navigate(page);
        }
    }
}
