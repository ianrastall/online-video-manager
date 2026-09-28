namespace OnlineVideoManager.ViewModels.Services;

public interface IPickerService
{
    Task<string?> PickFolderAsync();

    Task<string?> PickTextFileAsync();
}
