using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OnlineVideoManager.Core.Links;
using OnlineVideoManager.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace OnlineVideoManager.App.Views;

public sealed partial class DownloadsPage : Page
{
    public DownloadsPage()
    {
        ViewModel = App.GetService<DownloadsViewModel>();
        Inbox = App.GetService<ClipboardInboxViewModel>();
        InitializeComponent();
    }

    public DownloadsViewModel ViewModel { get; }

    public ClipboardInboxViewModel Inbox { get; }

    // Links dragged from a browser's address bar or a page arrive as text or as a web link.
    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.Text) || e.DataView.Contains(StandardDataFormats.WebLink))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Add to queue";
        }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        try
        {
            string? text = null;
            if (e.DataView.Contains(StandardDataFormats.WebLink))
            {
                text = (await e.DataView.GetWebLinkAsync())?.ToString();
            }

            if (text is null && e.DataView.Contains(StandardDataFormats.Text))
            {
                text = await e.DataView.GetTextAsync();
            }

            var links = LinkExtractor.FromText(text);
            if (links.Count > 0)
            {
                ViewModel.Report(ViewModel.Enqueue(links, ViewModel.AddMode), "");
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            App.GetService<NotificationViewModel>().Show($"Could not read what was dropped: {ex.Message}", NotificationLevel.Warning);
        }
    }
}
