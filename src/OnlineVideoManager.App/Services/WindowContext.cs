namespace OnlineVideoManager.App.Services;

/// <summary>The main window's handle, for APIs (such as pickers) that need an owner window.</summary>
public sealed class WindowContext
{
    public nint Handle { get; set; }
}
