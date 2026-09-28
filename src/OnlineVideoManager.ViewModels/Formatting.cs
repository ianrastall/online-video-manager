using System.Globalization;

namespace OnlineVideoManager.ViewModels;

internal static class Formatting
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Bytes(double bytes)
    {
        var unit = 0;
        while (bytes >= 1024 && unit < Units.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }

        return unit == 0
            ? string.Create(CultureInfo.CurrentCulture, $"{bytes:0} {Units[unit]}")
            : string.Create(CultureInfo.CurrentCulture, $"{bytes:0.0} {Units[unit]}");
    }

    public static string Speed(double bytesPerSecond) => Bytes(bytesPerSecond) + "/s";

    public static string Duration(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, Math.Round(seconds)));
        return t.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{t.Minutes}:{t.Seconds:00}");
    }

    public static string Host(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? (uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host)
            : "";

    public static string Plural(int count, string singular, string? plural = null) =>
        string.Create(CultureInfo.CurrentCulture, $"{count} {(count == 1 ? singular : plural ?? singular + "s")}");
}
