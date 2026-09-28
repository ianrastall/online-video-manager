using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OnlineVideoManager.ViewModels.Services;

namespace OnlineVideoManager.ViewModels;

public sealed partial class ToolsViewModel : ObservableObject
{
    private readonly EngineSession _engine;
    private readonly IShellService _shell;
    private readonly NotificationViewModel _notifications;
    public ToolsViewModel(EngineSession engine, IShellService shell, NotificationViewModel notifications)
    {
        _engine = engine; _shell = shell; _notifications = notifications;
        engine.Changed += (_, _) => Refresh();
        Refresh();
    }
    public ObservableCollection<ToolItemViewModel> Tools { get; } = [];
    public string[] LogLines => _engine.State.ToolLogs;
    public string ToolsDirectory => _engine.State.ToolsDirectory;
    public bool IsBusy => _engine.State.ToolsBusy;
    public string StatusText => _engine.State.ToolStatus;
    private void Refresh()
    {
        foreach (var state in _engine.State.Tools)
        {
            var tool = Tools.FirstOrDefault(t => t.Id == state.Id);
            if (tool is null) Tools.Add(new(this, state)); else tool.Apply(state);
        }
        OnPropertyChanged((string?)null);
        CheckForUpdatesCommand.NotifyCanExecuteChanged(); UpdateAllCommand.NotifyCanExecuteChanged();
    }
    private bool CanRun() => !IsBusy;
    [RelayCommand(CanExecute = nameof(CanRun))] private Task CheckForUpdatesAsync() => _engine.SendAsync(new("tools.check"));
    [RelayCommand(CanExecute = nameof(CanRun))] private Task UpdateAllAsync() => _engine.SendAsync(new("tools.update"));
    [RelayCommand] private Task CancelAsync() => _engine.SendAsync(new("tools.cancel"));
    [RelayCommand]
    private async Task OpenToolsFolderAsync()
    {
        try { _shell.OpenFolder((await _engine.ExecuteAsync(new("folder.prepare", Kind: "tools"))).Path!); }
        catch (Exception ex) { _notifications.Show(ex.Message, NotificationLevel.Error); }
    }
    internal Task InstallAsync(string id) => _engine.SendAsync(new("tools.update", Tool: id));
}
