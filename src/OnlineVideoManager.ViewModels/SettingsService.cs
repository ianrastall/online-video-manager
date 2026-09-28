using OnlineVideoManager.Core.Settings;

namespace OnlineVideoManager.ViewModels;

/// <summary>Owns the live <see cref="AppSettings"/> instance and tells interested view models when it changes.</summary>
public sealed class SettingsService(AppSettings current, SettingsStore store)
{
    public AppSettings Current { get; } = current;

    public string ToolsDirectory => Current.ResolvedToolsDirectory;
    public string DataDirectory => Path.GetDirectoryName(Path.GetFullPath(store.Path))!;

    public event EventHandler? Changed;

    /// <summary>Write the settings file and raise <see cref="Changed"/>. Returns false with
    /// <paramref name="error"/> set when the file could not be written; the in-memory settings
    /// still apply for this session.</summary>
    public bool Save(out string? error)
    {
        error = null;
        try
        {
            store.Save(Current);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return error is null;
    }
}
