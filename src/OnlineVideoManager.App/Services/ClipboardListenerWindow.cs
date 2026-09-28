using System.Runtime.InteropServices;
using static OnlineVideoManager.App.Services.NativeMethods;

namespace OnlineVideoManager.App.Services;

/// <summary>
/// A hidden message-only window registered with AddClipboardFormatListener. Windows sends it
/// WM_CLIPBOARDUPDATE on every clipboard change, whether or not the app is in the foreground.
/// Must be created on the UI thread, whose message loop delivers its messages.
/// </summary>
internal sealed class ClipboardListenerWindow : IDisposable
{
    private readonly WndProc _wndProc; // Held so the delegate outlives the native window.
    private readonly string _className = "OVM.ClipboardListener." + Environment.ProcessId;
    private readonly nint _instance = GetModuleHandle(null);
    private bool _disposed;

    public ClipboardListenerWindow()
    {
        _wndProc = WindowProc;
        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = _instance,
            lpszClassName = _className,
        };
        if (RegisterClassEx(ref wc) == 0)
        {
            throw new InvalidOperationException($"RegisterClassEx failed ({Marshal.GetLastWin32Error()}).");
        }

        Handle = CreateWindowEx(0, _className, "", 0, 0, 0, 0, 0, HWND_MESSAGE, 0, _instance, 0);
        if (Handle == 0)
        {
            throw new InvalidOperationException($"CreateWindowEx failed ({Marshal.GetLastWin32Error()}).");
        }

        if (!AddClipboardFormatListener(Handle))
        {
            throw new InvalidOperationException($"AddClipboardFormatListener failed ({Marshal.GetLastWin32Error()}).");
        }
    }

    public nint Handle { get; }

    public event EventHandler? ClipboardUpdated;

    private nint WindowProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WM_CLIPBOARDUPDATE)
        {
            try
            {
                ClipboardUpdated?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                // Never let an exception unwind into native code.
                CrashLog.Write(ex);
            }

            return 0;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        RemoveClipboardFormatListener(Handle);
        DestroyWindow(Handle);
        UnregisterClass(_className, _instance);
    }
}
