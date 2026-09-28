using static OnlineVideoManager.App.Services.NativeMethods;

namespace OnlineVideoManager.App.Services;

/// <summary>One running copy per user session, so there is one queue file and one clipboard watcher.</summary>
internal static class SingleInstance
{
    private static Mutex? _mutex;

    public static bool TryClaim()
    {
        _mutex = new Mutex(initiallyOwned: true, @"Local\OnlineVideoManager.SingleInstance", out var created);
        if (!created)
        {
            _mutex.Dispose();
            _mutex = null;
        }

        return created;
    }

    public static void ActivateExisting(string windowTitle)
    {
        var hwnd = FindWindow(null, windowTitle);
        if (hwnd == 0)
        {
            return;
        }

        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, SW_RESTORE);
        }

        SetForegroundWindow(hwnd);
    }
}
