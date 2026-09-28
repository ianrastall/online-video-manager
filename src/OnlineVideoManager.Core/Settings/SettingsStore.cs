using System.Text.Json;

namespace OnlineVideoManager.Core.Settings;

public sealed class SettingsStore(string path)
{
    public string Path { get; } = path;

    public static SettingsStore Default => new(AppPaths.SettingsFile);

    /// <summary>Load settings. A missing file gives defaults; an unreadable one is moved aside
    /// (so the user's file is not silently overwritten) and defaults are used.</summary>
    public AppSettings Load()
    {
        AppSettings settings;
        try
        {
            settings = File.Exists(Path)
                ? JsonSerializer.Deserialize(File.ReadAllText(Path), CoreJsonContext.Default.AppSettings) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            TryMoveAside();
            settings = new AppSettings();
        }

        settings.Normalize();
        return settings;
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, CoreJsonContext.Default.AppSettings));
        File.Move(temp, Path, overwrite: true);
    }

    private void TryMoveAside()
    {
        try
        {
            File.Move(Path, Path + ".invalid", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
