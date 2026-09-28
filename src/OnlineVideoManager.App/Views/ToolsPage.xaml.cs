using Microsoft.UI.Xaml.Controls;
using OnlineVideoManager.ViewModels;

namespace OnlineVideoManager.App.Views;

public sealed partial class ToolsPage : Page
{
    public ToolsPage()
    {
        ViewModel = App.GetService<ToolsViewModel>();
        InitializeComponent();
    }

    public ToolsViewModel ViewModel { get; }
}
