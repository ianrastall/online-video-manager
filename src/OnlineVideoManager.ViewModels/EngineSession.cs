using OnlineVideoManager.Contracts;

namespace OnlineVideoManager.ViewModels;

/// <summary>UI-thread snapshot projection. It owns no queue, download or persistence policy.</summary>
public sealed class EngineSession
{
    private readonly IEngine _engine;
    private readonly NotificationViewModel _notifications;
    private ulong _noticeId;
    public EngineSession(IEngine engine, NotificationViewModel notifications)
    {
        _engine = engine; _notifications = notifications;
        State = engine.Execute(new("snapshot")).State!;
    }
    public EngineSnapshot State { get; private set; }
    public event EventHandler? Changed;
    private EngineReply Accept(EngineReply reply)
    {
        if (reply.State is { } state && state.Revision >= State.Revision)
        {
            State = state;
            if (state.NoticeId > _noticeId) { _noticeId = state.NoticeId; _notifications.Show(state.Notice); }
            Changed?.Invoke(this, EventArgs.Empty);
        }
        return reply;
    }
    public EngineReply Execute(EngineRequest request) => Accept(_engine.Execute(request));
    public async Task<EngineReply> ExecuteAsync(EngineRequest request) => Accept(await _engine.ExecuteAsync(request));
    public async Task SendAsync(EngineRequest request)
    {
        try { await ExecuteAsync(request); }
        catch (Exception ex) { _notifications.Show(ex.Message, NotificationLevel.Error); }
    }
}
