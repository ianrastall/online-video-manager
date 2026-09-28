namespace OnlineVideoManager.Core.Settings;

public enum DownloadMode
{
    Video,
    Audio,
}

public enum VideoContainer
{
    /// <summary>Matroska holds any codec combination, so the best streams never need re-encoding.</summary>
    Mkv,
    Mp4,
    /// <summary>Let yt-dlp choose based on the selected streams.</summary>
    Auto,
}

public enum AudioFormat
{
    /// <summary>Keep the original stream (no re-encode).</summary>
    Best,
    Opus,
    M4a,
    Mp3,
    Flac,
}

public enum UpdateChannel
{
    Stable,
    Nightly,
}

public enum VideoCodec { Best, H264, Hevc, Av1, Vp9 }

public enum LinkFilterMode
{
    /// <summary>Only links on known video sites (plus user-added sites) are picked up from the clipboard.</summary>
    KnownSites,
    /// <summary>Every http(s) link on the clipboard is picked up.</summary>
    AnyLink,
}
