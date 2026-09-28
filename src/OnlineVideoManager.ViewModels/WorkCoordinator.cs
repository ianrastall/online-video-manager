namespace OnlineVideoManager.ViewModels;

/// <summary>
/// Keeps downloads and tool updates from stepping on each other: while tools are being
/// replaced no new download starts, and an install waits until running downloads finish.
/// Used only on the UI thread.
/// </summary>
public sealed class WorkCoordinator
{
    private readonly List<TaskCompletionSource> _idleWaiters = [];

    public bool ToolsBusy { get; private set; }

    public int ActiveDownloads { get; private set; }

    /// <summary>Raised when <see cref="ToolsBusy"/> or <see cref="ActiveDownloads"/> changes.</summary>
    public event EventHandler? Changed;

    public void SetToolsBusy(bool busy)
    {
        if (ToolsBusy == busy)
        {
            return;
        }

        ToolsBusy = busy;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void DownloadStarted()
    {
        ActiveDownloads++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void DownloadFinished()
    {
        ActiveDownloads = Math.Max(0, ActiveDownloads - 1);
        if (ActiveDownloads == 0)
        {
            foreach (var waiter in _idleWaiters)
            {
                waiter.TrySetResult();
            }

            _idleWaiters.Clear();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Completes once no download is running.</summary>
    public Task WaitForDownloadsIdleAsync()
    {
        if (ActiveDownloads == 0)
        {
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _idleWaiters.Add(tcs);
        return tcs.Task;
    }
}
