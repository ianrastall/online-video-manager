namespace OnlineVideoManager.Core.Links;

/// <summary>
/// A comparison key for links, so the same video copied in different forms
/// (youtu.be vs. youtube.com/watch, with or without tracking parameters) counts once.
/// </summary>
public static class LinkKey
{
    public static string For(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return url.Trim();
        }

        var youtubeId = YouTubeVideoId(uri);
        if (youtubeId is not null)
        {
            return "youtube:" + youtubeId;
        }

        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal))
        {
            host = host[4..];
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        return host + path + uri.Query;
    }

    /// <summary>The video ID for single-video YouTube links; null for anything else
    /// (playlists, channels, other sites).</summary>
    public static string? YouTubeVideoId(Uri uri)
    {
        var host = uri.Host.ToLowerInvariant();
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (host is "youtu.be" or "www.youtu.be")
        {
            return segments.Length > 0 ? ValidId(segments[0]) : null;
        }

        if (!KnownVideoSites.HostMatches(host, "youtube.com") && !KnownVideoSites.HostMatches(host, "youtube-nocookie.com"))
        {
            return null;
        }

        if (segments.Length >= 1 && segments[0].Equals("watch", StringComparison.OrdinalIgnoreCase))
        {
            return ValidId(QueryValue(uri.Query, "v"));
        }

        if (segments.Length >= 2 && segments[0].ToLowerInvariant() is "shorts" or "live" or "embed" or "v")
        {
            return ValidId(segments[1]);
        }

        return null;
    }

    private static string? QueryValue(string query, string name)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var key = eq < 0 ? pair : pair[..eq];
            if (key.Equals(name, StringComparison.Ordinal))
            {
                return eq < 0 ? "" : Uri.UnescapeDataString(pair[(eq + 1)..]);
            }
        }

        return null;
    }

    private static string? ValidId(string? id) =>
        id is { Length: 11 } && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ? id : null;
}
