namespace OnlineVideoManager.ViewModels.Services;

/// <summary>One clipboard entry, in the text flavours that can carry links.</summary>
public sealed record ClipboardSnapshot(string? Text, string? Html);

public enum ClipboardHistoryStatus
{
    Success,
    Disabled,
    AccessDenied,
    Unavailable,
}

public sealed record ClipboardHistoryResult(ClipboardHistoryStatus Status, IReadOnlyList<ClipboardSnapshot> Items);

public interface IClipboardService
{
    /// <summary>Raised on the UI thread whenever the system clipboard changes, including while the
    /// app is in the background.</summary>
    event EventHandler? ContentChanged;

    /// <summary>The current clipboard entry, or null when it holds nothing text-like, cannot be
    /// read, or was marked private by the app that set it (e.g. a password manager).</summary>
    Task<ClipboardSnapshot?> ReadAsync();

    /// <summary>Entries from Windows clipboard history (Win+V), newest first.</summary>
    Task<ClipboardHistoryResult> ReadHistoryAsync();

    void SetText(string text);
}
