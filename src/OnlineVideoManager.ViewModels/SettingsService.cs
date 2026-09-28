using OnlineVideoManager.Contracts;

namespace OnlineVideoManager.ViewModels;

public sealed class SettingsService
{
    private readonly EngineSession _engine;
    public SettingsService(EngineSession engine)
    {
        _engine = engine;
        engine.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }
    public AppSettings Current => _engine.State.Settings;
    public string ToolsDirectory => _engine.State.ToolsDirectory;
    public string DataDirectory => _engine.State.DataDirectory;
    public string[] KnownSites => _engine.State.KnownSites;
    public event EventHandler? Changed;
    public bool Save(out string? error)
    {
        try { _engine.Execute(new("settings.set", Settings: Current)); error = null; return true; }
        catch (Exception ex)
        {
            error = ex.Message;
            _engine.Execute(new("snapshot"));
            return false;
        }
    }
    public void ResetOutputTemplate() => _engine.Execute(new("settings.resetTemplate"));
}
