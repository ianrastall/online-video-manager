using OnlineVideoManager.ViewModels.Services;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace OnlineVideoManager.App.Services;

internal sealed class PickerService(WindowContext window) : IPickerService
{
    public async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.VideosLibrary };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, window.Handle);
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    public async Task<string?> PickTextFileAsync()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        foreach (var type in new[] { ".txt", ".md", ".csv", ".html", ".htm", "*" })
        {
            picker.FileTypeFilter.Add(type);
        }

        InitializeWithWindow.Initialize(picker, window.Handle);
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }
}
