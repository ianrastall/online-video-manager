using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
        if (e.DataView.Contains(StandardDataFormats.Text) || e.DataView.Contains(StandardDataFormats.WebLink)
            || e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Add to queue";
        }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        try
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                foreach (var item in await e.DataView.GetStorageItemsAsync())
                    if (item is Windows.Storage.StorageFile file && file.FileType.Equals(".txt", StringComparison.OrdinalIgnoreCase))
                        await ViewModel.ImportPathAsync(file.Path);
                return;
            }
            string? text = null;
            if (e.DataView.Contains(StandardDataFormats.WebLink))
            {
                text = (await e.DataView.GetWebLinkAsync())?.ToString();
            }

            if (text is null && e.DataView.Contains(StandardDataFormats.Text))
            {
                text = await e.DataView.GetTextAsync();
            }

            if (text is not null) await ViewModel.AddTextAsync(text);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            App.GetService<NotificationViewModel>().Show($"Could not read what was dropped: {ex.Message}", NotificationLevel.Warning);
        }
    }
}
