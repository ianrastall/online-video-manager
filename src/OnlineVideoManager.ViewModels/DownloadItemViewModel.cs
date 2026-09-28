using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OnlineVideoManager.Contracts;

namespace OnlineVideoManager.ViewModels;

/// <summary>Display formatting only; the native engine owns state transitions and progress.</summary>
public sealed partial class DownloadItemViewModel : ObservableObject
{
    private readonly DownloadsViewModel _owner;
    private DownloadSnapshot _state;
    internal DownloadItemViewModel(DownloadsViewModel owner, DownloadSnapshot state) { _owner = owner; _state = state; }
    public string Id => _state.Id;
    public string Url => _state.Url;
    public string Host => Formatting.Host(Url);
    public string DisplayName => string.IsNullOrEmpty(_state.Title) ? Url : _state.Title;
    public DownloadStatus Status => _state.Status;
    public string ModeGlyph => _state.Mode == DownloadMode.Audio ? "\uE8D6" : "\uE714";
    public string ModeText => _state.Mode == DownloadMode.Audio ? "Audio" : "Video";
    public string FilePath => _state.FilePath;
    public bool HasFile => FilePath.Length > 0;
    public string ErrorMessage => _state.Error;
    public bool HasError => ErrorMessage.Length > 0;
    public bool CanCancel => Status is DownloadStatus.Queued or DownloadStatus.Running;
    public bool CanRetry => Status is DownloadStatus.Failed or DownloadStatus.Canceled or DownloadStatus.Completed;
    public bool ShowProgress => Status is DownloadStatus.Running or DownloadStatus.Completed;
    public double ProgressValue => _state.Progress;
    public bool IsProgressIndeterminate => _state.Indeterminate;
    public string[] LogLines => _state.Logs;
    [ObservableProperty] public partial bool IsLogVisible { get; set; }
    public string StatusText => Status switch
    {
        DownloadStatus.Queued => "Waiting",
        DownloadStatus.Running => string.IsNullOrEmpty(_state.Stage) ? "Downloading" : _state.Stage,
        DownloadStatus.Completed => "Done",
        _ => Status.ToString()
    };
    public string DetailText
    {
        get
        {
            var parts = new List<string>();
            if (_state.PlaylistCount > 0) parts.Add($"Item {_state.PlaylistIndex} of {_state.PlaylistCount}");
            if (_state.Streams > 1) parts.Add($"Stream {_state.Stream} of {_state.Streams}");
            if (_state.Downloaded is { } d) parts.Add(_state.Total is { } t ? $"{Formatting.Bytes(d)} of {Formatting.Bytes(t)}" : Formatting.Bytes(d));
            if (_state.Speed is { } s) parts.Add(Formatting.Speed(s));
            if (_state.Eta is { } e) parts.Add(Formatting.Duration(e) + " left");
            return string.Join(" · ", parts);
        }
    }
    internal void Apply(DownloadSnapshot state)
    {
        _state = state;
        OnPropertyChanged((string?)null);
        CancelCommand.NotifyCanExecuteChanged(); RetryCommand.NotifyCanExecuteChanged();
        OpenFileCommand.NotifyCanExecuteChanged(); RevealFileCommand.NotifyCanExecuteChanged();
    }
    [RelayCommand(CanExecute = nameof(CanCancel))] private Task CancelAsync() => _owner.CancelAsync(this);
    [RelayCommand(CanExecute = nameof(CanRetry))] private Task RetryAsync() => _owner.RetryAsync(this);
    [RelayCommand] private Task RemoveAsync() => _owner.RemoveAsync(this);
    [RelayCommand(CanExecute = nameof(HasFile))] private void OpenFile() => _owner.OpenFile(this);
    [RelayCommand(CanExecute = nameof(HasFile))] private void RevealFile() => _owner.RevealFile(this);
    [RelayCommand] private void CopyUrl() => _owner.CopyUrl(this);
    [RelayCommand] private void ToggleLog() => IsLogVisible = !IsLogVisible;
}
