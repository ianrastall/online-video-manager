using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OnlineVideoManager.Contracts;

namespace OnlineVideoManager.ViewModels;

public sealed partial class ToolItemViewModel : ObservableObject
{
    private readonly ToolsViewModel _owner;
    private ToolSnapshot _state;
    internal ToolItemViewModel(ToolsViewModel owner, ToolSnapshot state) { _owner = owner; _state = state; }
    public string Id => _state.Id;
    public string Name => Id == "ffmpeg" ? "FFmpeg + ffprobe" : Id == "deno" ? "Deno" : "yt-dlp";
    public string Description => Id switch { "ffmpeg" => "Media conversion and inspection", "deno" => "JavaScript runtime for video extraction", _ => "Video and audio downloader" };
    public bool IsWorking => _state.Working;
    public bool IsInstalled => _state.Installed.Length > 0;
    public string InstalledText => IsInstalled ? _state.Installed : "Not installed";
    public string LatestText => _state.Latest.Length > 0 ? _state.Latest : "Not checked";
    public bool UpdateAvailable => IsInstalled && _state.Latest.Length > 0 && _state.Latest != _state.Installed;
    public string StatusText => IsWorking ? _state.Activity : _state.Error.Length > 0 ? _state.Error : !IsInstalled ? "Not installed" : UpdateAvailable ? "Update available" : "Installed";
    public string ActionText => !IsInstalled ? "Install" : UpdateAvailable ? "Update" : "Reinstall";
    public string Detail => Id == "ffmpeg" && IsInstalled ? "Includes ffprobe" : "";
    public double ProgressValue => Math.Max(0, _state.Progress) * 100;
    public bool IsProgressIndeterminate => _state.Progress < 0;
    private bool CanInstall() => !IsWorking && !_owner.IsBusy;
    [RelayCommand(CanExecute = nameof(CanInstall))] private Task InstallAsync() => _owner.InstallAsync(Id);
    internal void Apply(ToolSnapshot state) { _state = state; OnPropertyChanged((string?)null); InstallCommand.NotifyCanExecuteChanged(); }
}
