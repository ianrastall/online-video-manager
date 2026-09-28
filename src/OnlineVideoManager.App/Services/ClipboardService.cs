using System.Runtime.InteropServices;
using System.Text;
using OnlineVideoManager.ViewModels.Services;
using Windows.ApplicationModel.DataTransfer;
using static OnlineVideoManager.App.Services.NativeMethods;

namespace OnlineVideoManager.App.Services;

/// <summary>
/// Clipboard access. Change notifications and reads use the Win32 clipboard, which works
/// while the app is in the background; clipboard history (Win+V) is only exposed through
/// the WinRT API, which Windows allows while the app is in the foreground.
/// </summary>
internal sealed class ClipboardService : IClipboardService, IDisposable
{
    /// <summary>Set by password managers and similar apps to ask clipboard monitors to look away.</summary>
    private static readonly uint ExcludeFormat = RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
    private static readonly uint HtmlFormat = RegisterClipboardFormat("HTML Format");

    private readonly ClipboardListenerWindow _listener;

    public ClipboardService()
    {
        _listener = new ClipboardListenerWindow();
        _listener.ClipboardUpdated += (_, _) => ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? ContentChanged;

    public async Task<ClipboardSnapshot?> ReadAsync()
    {
        // Another process may hold the clipboard open for a moment right after changing it.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (TryReadWin32(out var snapshot))
            {
                return snapshot;
            }

            await Task.Delay(40 * (attempt + 1));
        }

        return null;
    }

    private bool TryReadWin32(out ClipboardSnapshot? snapshot)
    {
        snapshot = null;
        if (!OpenClipboard(_listener.Handle))
        {
            return false;
        }

        try
        {
            if (IsClipboardFormatAvailable(ExcludeFormat))
            {
                return true;
            }

            string? text = null;
            if (IsClipboardFormatAvailable(CF_UNICODETEXT))
            {
                text = ReadGlobal(GetClipboardData(CF_UNICODETEXT), bytes => Encoding.Unicode.GetString(bytes));
            }

            string? html = null;
            if (HtmlFormat != 0 && IsClipboardFormatAvailable(HtmlFormat))
            {
                html = ReadGlobal(GetClipboardData(HtmlFormat), bytes => Encoding.UTF8.GetString(bytes));
            }

            if (text is not null || html is not null)
            {
                snapshot = new ClipboardSnapshot(text, html);
            }

            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static string? ReadGlobal(nint handle, Func<byte[], string> decode)
    {
        if (handle == 0)
        {
            return null;
        }

        var pointer = GlobalLock(handle);
        if (pointer == 0)
        {
            return null;
        }

        try
        {
            var size = (int)Math.Min((ulong)GlobalSize(handle), int.MaxValue);
            var bytes = new byte[size];
            Marshal.Copy(pointer, bytes, 0, size);
            var text = decode(bytes);
            var nul = text.IndexOf('\0');
            return nul >= 0 ? text[..nul] : text;
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    public async Task<ClipboardHistoryResult> ReadHistoryAsync()
    {
        try
        {
            if (!Clipboard.IsHistoryEnabled())
            {
                return new ClipboardHistoryResult(ClipboardHistoryStatus.Disabled, []);
            }

            var result = await Clipboard.GetHistoryItemsAsync();
            switch (result.Status)
            {
                case ClipboardHistoryItemsResultStatus.AccessDenied:
                    return new ClipboardHistoryResult(ClipboardHistoryStatus.AccessDenied, []);
                case ClipboardHistoryItemsResultStatus.ClipboardHistoryDisabled:
                    return new ClipboardHistoryResult(ClipboardHistoryStatus.Disabled, []);
            }

            var items = new List<ClipboardSnapshot>();
            foreach (var item in result.Items)
            {
                try
                {
                    var view = item.Content;
                    string? text = view.Contains(StandardDataFormats.Text) ? await view.GetTextAsync() : null;
                    string? html = view.Contains(StandardDataFormats.Html) ? await view.GetHtmlFormatAsync() : null;
                    if (text is not null || html is not null)
                    {
                        items.Add(new ClipboardSnapshot(text, html));
                    }
                }
                catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
                {
                    // Skip entries that can no longer be rendered.
                }
            }

            return new ClipboardHistoryResult(ClipboardHistoryStatus.Success, items);
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or TypeLoadException)
        {
            return new ClipboardHistoryResult(ClipboardHistoryStatus.Unavailable, []);
        }
    }

    public void SetText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    public void Dispose() => _listener.Dispose();
}
