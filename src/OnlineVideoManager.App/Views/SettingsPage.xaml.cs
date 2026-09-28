using Microsoft.UI.Xaml.Controls;
using OnlineVideoManager.ViewModels;

namespace OnlineVideoManager.App.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        InitializeComponent();
    }

    public SettingsViewModel ViewModel { get; }
}
