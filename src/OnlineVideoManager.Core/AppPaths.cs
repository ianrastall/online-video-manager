namespace OnlineVideoManager.Core;

/// <summary>
/// Where the app keeps its settings, queue and tools.
/// Everything lives under one per-user folder so nothing is scattered across the system.
/// </summary>
public static class AppPaths
{
    public const string AppFolderName = "OnlineVideoManager";

    /// <summary>Set this environment variable to relocate all app data (portable use, testing).</summary>
    public const string HomeOverrideVariable = "OVM_HOME";

    public static string DataDirectory
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable(HomeOverrideVariable);
            if (!string.IsNullOrWhiteSpace(overridden))
            {
                return overridden;
            }

            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(local))
            {
                local = AppContext.BaseDirectory;
            }

            return Path.Combine(local, AppFolderName);
        }
    }

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static string QueueFile => Path.Combine(DataDirectory, "queue.json");

    public static string DefaultToolsDirectory => Path.Combine(DataDirectory, "tools");

    public static string DefaultOutputDirectory
    {
        get
        {
            var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            if (string.IsNullOrEmpty(videos))
            {
                videos = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            return Path.Combine(videos, "OVM");
        }
    }

    /// <summary>
    /// The configured directory or writable per-user storage. Never use the read-only MSIX payload.
    /// </summary>
    public static string ResolveToolsDirectory(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return DefaultToolsDirectory;
    }
}
