namespace OnlineVideoManager.ViewModels.Services;

public interface IShellService
{
    void OpenFolder(string path);

    /// <summary>Open Explorer with the file selected.</summary>
    void RevealFile(string path);

    void OpenFile(string path);
}
