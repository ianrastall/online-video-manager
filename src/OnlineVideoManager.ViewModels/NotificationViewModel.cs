using CommunityToolkit.Mvvm.ComponentModel;

namespace OnlineVideoManager.ViewModels;

public enum NotificationLevel
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>The single status banner at the top of the window.</summary>
public sealed partial class NotificationViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Message { get; set; } = "";

    [ObservableProperty]
    public partial NotificationLevel Level { get; set; }

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    public void Show(string message, NotificationLevel level = NotificationLevel.Info)
    {
        // Re-open even if the same text is shown again after being dismissed.
        IsOpen = false;
        Message = message;
        Level = level;
        IsOpen = true;
    }

    public void Dismiss() => IsOpen = false;
}
