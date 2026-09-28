using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OnlineVideoManager.ViewModels;

namespace OnlineVideoManager.App;

/// <summary>Conversion functions for x:Bind.</summary>
public static class XamlConvert
{
    public static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility Collapsed(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static bool Not(bool value) => !value;

    public static InfoBarSeverity Severity(NotificationLevel level) => level switch
    {
        NotificationLevel.Success => InfoBarSeverity.Success,
        NotificationLevel.Warning => InfoBarSeverity.Warning,
        NotificationLevel.Error => InfoBarSeverity.Error,
        _ => InfoBarSeverity.Informational,
    };
}
