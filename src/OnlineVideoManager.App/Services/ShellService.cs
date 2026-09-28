using System.ComponentModel;
using System.Diagnostics;
using OnlineVideoManager.ViewModels;
using OnlineVideoManager.ViewModels.Services;

namespace OnlineVideoManager.App.Services;

internal sealed class ShellService(NotificationViewModel notifications) : IShellService
{
    public void OpenFolder(string path) => Run(new ProcessStartInfo(path) { UseShellExecute = true });

    // explorer.exe parses "/select,<path>" itself, so this must be one pre-quoted argument string.
    public void RevealFile(string path) => Run(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false });

    public void OpenFile(string path) => Run(new ProcessStartInfo(path) { UseShellExecute = true });

    private void Run(ProcessStartInfo startInfo)
    {
        try
        {
            using var _ = Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            notifications.Show($"Windows could not open it: {ex.Message}", NotificationLevel.Error);
        }
    }
}
