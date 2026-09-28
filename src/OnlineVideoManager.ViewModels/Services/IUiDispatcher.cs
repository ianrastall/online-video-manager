namespace OnlineVideoManager.ViewModels.Services;

/// <summary>Runs work on the UI thread. View models touch bound state only through this.</summary>
public interface IUiDispatcher
{
    /// <summary>Queue <paramref name="action"/> to run on the UI thread, in order.</summary>
    void Post(Action action);
}
