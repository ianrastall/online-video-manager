using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OnlineVideoManager.Core.Downloads;
using OnlineVideoManager.Core.Settings;

namespace OnlineVideoManager.ViewModels;

public enum DownloadStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Canceled,
}

/// <summary>One entry in the download queue. All members are used on the UI thread.</summary>
public sealed partial class DownloadItemViewModel : ObservableObject
{
    private const int MaxLogLines = 300;

    private readonly DownloadsViewModel _owner;

    // Raw progress state reported by yt-dlp.
    private int _streams;
    private int _stream;
    private int _playlistIndex;
    private int _playlistCount;
    private double? _downloaded;
    private double? _total;
    private double? _speed;
    private double? _eta;
    private string? _stage;

    internal DownloadItemViewModel(DownloadsViewModel owner, string url, DownloadMode mode)
    {
        _owner = owner;
        Url = url;
        Mode = mode;
        Host = Formatting.Host(url);
    }

    public string Url { get; }

    public DownloadMode Mode { get; }

    public string Host { get; }

    public bool IsAudio => Mode == DownloadMode.Audio;

    /// <summary>Segoe Fluent Icons glyph: Audio or Video.</summary>
    public string ModeGlyph => IsAudio ? "" : "";

    public string ModeText => IsAudio ? "Audio" : "Video";

    public ObservableCollection<string> LogLines { get; } = [];

    internal CancellationTokenSource? Cancellation { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    public partial string? Title { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Title) ? Url : Title;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsRunning), nameof(CanCancel), nameof(CanRetry), nameof(IsFinished), nameof(ShowProgress))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand), nameof(RetryCommand))]
    public partial DownloadStatus Status { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial bool IsCanceling { get; set; }

    [ObservableProperty]
    public partial double ProgressValue { get; set; }

    [ObservableProperty]
    public partial bool IsProgressIndeterminate { get; set; }

    [ObservableProperty]
    public partial string DetailText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFile))]
    [NotifyCanExecuteChangedFor(nameof(OpenFileCommand), nameof(RevealFileCommand))]
    public partial string? FilePath { get; set; }

    [ObservableProperty]
    public partial bool IsLogVisible { get; set; }

    public bool IsRunning => Status == DownloadStatus.Running;

    public bool CanCancel => Status is DownloadStatus.Queued or DownloadStatus.Running;

    public bool CanRetry => Status is DownloadStatus.Failed or DownloadStatus.Canceled or DownloadStatus.Completed;

    public bool IsFinished => Status is DownloadStatus.Completed or DownloadStatus.Failed or DownloadStatus.Canceled;

    public bool ShowProgress => Status is DownloadStatus.Running or DownloadStatus.Completed;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool HasFile => !string.IsNullOrEmpty(FilePath);

    public string StatusText => Status switch
    {
        DownloadStatus.Queued => "Waiting",
        DownloadStatus.Running when IsCanceling => "Canceling",
        DownloadStatus.Running => StageLabel(_stage) ?? (_downloaded is null ? "Starting" : "Downloading"),
        DownloadStatus.Completed => "Done",
        DownloadStatus.Failed => "Failed",
        DownloadStatus.Canceled => "Canceled",
        _ => "",
    };

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _owner.Cancel(this);

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private void Retry() => _owner.Retry(this);

    [RelayCommand]
    private void Remove() => _owner.Remove(this);

    [RelayCommand(CanExecute = nameof(HasFile))]
    private void OpenFile() => _owner.OpenFile(this);

    [RelayCommand(CanExecute = nameof(HasFile))]
    private void RevealFile() => _owner.RevealFile(this);

    [RelayCommand]
    private void CopyUrl() => _owner.CopyUrl(this);

    [RelayCommand]
    private void ToggleLog() => IsLogVisible = !IsLogVisible;

    // ----- state changes, driven by DownloadsViewModel ---------------------------

    internal void ResetForQueue()
    {
        _streams = _stream = _playlistIndex = _playlistCount = 0;
        _downloaded = _total = _speed = _eta = null;
        _stage = null;
        ErrorMessage = null;
        FilePath = null;
        ProgressValue = 0;
        IsProgressIndeterminate = false;
        IsCanceling = false;
        Status = DownloadStatus.Queued;
        DetailText = "";
    }

    internal void MarkRunning()
    {
        ResetForQueue();
        LogLines.Clear();
        Status = DownloadStatus.Running;
        IsProgressIndeterminate = true;
        RefreshProgress();
    }

    internal void Apply(JobUpdate update)
    {
        switch (update)
        {
            case JobUpdate.Title t:
                Title = t.Value;
                break;
            case JobUpdate.Streams s:
                _streams = s.Count;
                _stream = 0;
                break;
            case JobUpdate.Destination:
                _stream++;
                _stage = null;
                _downloaded = _total = _speed = _eta = null;
                break;
            case JobUpdate.Progress p:
                _downloaded = p.Downloaded;
                _total = p.Total ?? _total;
                _speed = p.Speed;
                _eta = p.Eta;
                break;
            case JobUpdate.PlaylistItem item:
                _playlistIndex = item.Index;
                _playlistCount = item.Count;
                _streams = _stream = 0;
                _downloaded = _total = null;
                break;
            case JobUpdate.Stage stage:
                _stage = stage.Name;
                _speed = _eta = null;
                break;
            case JobUpdate.FileSaved file:
                FilePath = file.Path;
                break;
            case JobUpdate.Log log:
                if (log.IsError)
                {
                    ErrorMessage = CleanError(log.Line);
                }

                AppendLog(log.Line);
                return;
        }

        RefreshProgress();
    }

    internal void Complete(int exitCode)
    {
        _speed = _eta = null;
        IsCanceling = false;
        if (exitCode == 0)
        {
            _stage = null;
            ErrorMessage = null;
            Status = DownloadStatus.Completed;
            ProgressValue = 100;
            IsProgressIndeterminate = false;
        }
        else
        {
            ErrorMessage ??= $"yt-dlp exited with code {exitCode}.";
            Status = DownloadStatus.Failed;
            IsProgressIndeterminate = false;
        }

        RefreshDetail();
        OnPropertyChanged(nameof(StatusText));
    }

    internal void MarkCanceled()
    {
        IsCanceling = false;
        IsProgressIndeterminate = false;
        Status = DownloadStatus.Canceled;
        DetailText = "";
    }

    internal void Fail(string message)
    {
        ErrorMessage = message;
        AppendLog(message);
        IsCanceling = false;
        IsProgressIndeterminate = false;
        Status = DownloadStatus.Failed;
        DetailText = "";
    }

    /// <summary>Restore the state of an entry saved in an earlier session.</summary>
    internal void Restore(SavedDownloadState state, string? error)
    {
        switch (state)
        {
            case SavedDownloadState.Failed:
                ErrorMessage = error;
                Status = DownloadStatus.Failed;
                break;
            case SavedDownloadState.Canceled:
                Status = DownloadStatus.Canceled;
                break;
            default:
                Status = DownloadStatus.Queued;
                break;
        }
    }

    private void AppendLog(string line)
    {
        if (LogLines.Count >= MaxLogLines)
        {
            LogLines.RemoveAt(0);
        }

        LogLines.Add(line);
    }

    private void RefreshProgress()
    {
        double? fraction = _downloaded is { } d && _total is > 0 ? Math.Clamp(d / _total.Value, 0, 1) : null;

        // Spread the bar across the streams being merged and the items of a playlist,
        // so it moves forward once instead of filling up once per stream.
        double? overall = fraction;
        if (overall is not null && _streams > 1 && _stream >= 1)
        {
            overall = (Math.Min(_stream, _streams) - 1 + overall.Value) / _streams;
        }

        if (overall is not null && _playlistCount > 1 && _playlistIndex >= 1)
        {
            overall = (Math.Min(_playlistIndex, _playlistCount) - 1 + overall.Value) / _playlistCount;
        }

        if (_stage is not null)
        {
            IsProgressIndeterminate = true;
        }
        else if (overall is { } value)
        {
            IsProgressIndeterminate = false;
            ProgressValue = value * 100;
        }

        RefreshDetail();
        OnPropertyChanged(nameof(StatusText));
    }

    private void RefreshDetail()
    {
        var parts = new List<string>(5);
        if (_playlistCount > 0)
        {
            parts.Add($"Item {_playlistIndex} of {_playlistCount}");
        }

        if (_streams > 1 && _stream >= 1 && _stage is null)
        {
            parts.Add($"Stream {Math.Min(_stream, _streams)} of {_streams}");
        }

        if (Status == DownloadStatus.Running && _stage is null)
        {
            if (_downloaded is { } d)
            {
                parts.Add(_total is { } t ? $"{Formatting.Bytes(d)} of {Formatting.Bytes(t)}" : Formatting.Bytes(d));
            }

            if (_speed is { } s)
            {
                parts.Add(Formatting.Speed(s));
            }

            if (_eta is { } e)
            {
                parts.Add(Formatting.Duration(e) + " left");
            }
        }

        DetailText = string.Join(" · ", parts);
    }

    private static string? StageLabel(string? stage) => stage switch
    {
        null => null,
        "Merger" => "Merging",
        "ExtractAudio" => "Extracting audio",
        "EmbedThumbnail" => "Embedding thumbnail",
        "Metadata" => "Writing metadata",
        "EmbedSubtitle" => "Embedding subtitles",
        "VideoConvertor" => "Converting",
        "VideoRemuxer" => "Remuxing",
        "ThumbnailsConvertor" => "Converting thumbnail",
        "MoveFiles" => "Moving files",
        "SponsorBlock" or "ModifyChapters" => "Processing chapters",
        _ when stage.StartsWith("Fixup", StringComparison.Ordinal) => "Fixing up",
        _ => stage,
    };

    private static string CleanError(string line) =>
        line.StartsWith("ERROR: ", StringComparison.Ordinal) ? line["ERROR: ".Length..] : line;
}
