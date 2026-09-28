using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OnlineVideoManager.Core.Tools;

namespace OnlineVideoManager.ViewModels;

public sealed partial class ToolItemViewModel : ObservableObject
{
    private readonly ToolsViewModel _owner;

    internal ToolItemViewModel(ToolsViewModel owner, ToolId id)
    {
        _owner = owner;
        Id = id;
    }

    public ToolId Id { get; }

    public string Name => Id.Name();

    public string Description => Id.Description();

    /// <summary>Installed version; null when not installed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstalled), nameof(InstalledText), nameof(UpdateAvailable), nameof(StatusText), nameof(ActionText))]
    public partial string? InstalledVersion { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LatestText), nameof(UpdateAvailable), nameof(StatusText), nameof(ActionText))]
    public partial ToolRelease? Latest { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LatestText), nameof(StatusText))]
    public partial string? LatestError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    public partial bool IsWorking { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial string ActivityText { get; set; } = "";

    [ObservableProperty]
    public partial double ProgressValue { get; set; }

    [ObservableProperty]
    public partial bool IsProgressIndeterminate { get; set; } = true;

    /// <summary>Extra detail line (ffmpeg: whether ffprobe is present).</summary>
    [ObservableProperty]
    public partial string Detail { get; set; } = "";

    public bool IsInstalled => InstalledVersion is not null;

    public string InstalledText => InstalledVersion ?? "Not installed";

    public string LatestText => Latest?.Version ?? (LatestError is null ? "Not checked" : "Check failed");

    public bool UpdateAvailable => IsInstalled && Latest is not null && InstalledVersion != Latest.Version;

    public string StatusText => IsWorking
        ? ActivityText
        : !IsInstalled ? "Not installed"
        : UpdateAvailable ? "Update available"
        : Latest is not null ? "Up to date"
        : LatestError is not null ? "Installed; could not check for updates"
        : "Installed";

    public string ActionText => !IsInstalled ? "Install" : UpdateAvailable ? "Update" : "Reinstall";

    private bool CanInstall() => !IsWorking && !_owner.IsBusy;

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private Task InstallAsync() => _owner.InstallAsync([this]);

    internal void RefreshCommands() => InstallCommand.NotifyCanExecuteChanged();
}
