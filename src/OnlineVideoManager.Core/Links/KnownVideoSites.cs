namespace OnlineVideoManager.Core.Links;

/// <summary>
/// Domains whose links are treated as downloadable when the clipboard is filtered to known sites.
/// yt-dlp supports far more sites than this; users add others in Settings, or switch the
/// clipboard filter to accept any link. A domain here also matches its subdomains.
/// </summary>
public static class KnownVideoSites
{
    public static IReadOnlyList<string> Domains { get; } =
    [
        "youtube.com", "youtu.be", "youtube-nocookie.com",
        "vimeo.com", "dailymotion.com", "dai.ly",
        "twitch.tv", "kick.com",
        "x.com", "twitter.com", "bsky.app", "threads.net", "threads.com",
        "tiktok.com", "instagram.com", "facebook.com", "fb.watch",
        "reddit.com", "redd.it",
        "soundcloud.com", "bandcamp.com", "mixcloud.com",
        "bilibili.com", "b23.tv", "nicovideo.jp", "nico.ms",
        "rumble.com", "odysee.com", "bitchute.com", "streamable.com",
        "archive.org", "vk.com", "vkvideo.ru", "ok.ru",
        "ted.com", "nebula.tv", "floatplane.com", "dropout.tv",
        "imgur.com", "9gag.com", "pinterest.com",
        "bbc.co.uk", "arte.tv", "ardmediathek.de", "zdf.de", "cbc.ca", "nhk.or.jp",
    ];

    public static bool HostMatches(string host, string domain)
    {
        host = host.TrimEnd('.');
        domain = domain.Trim().TrimStart('.').TrimEnd('.');
        return host.Equals(domain, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
    }
}
