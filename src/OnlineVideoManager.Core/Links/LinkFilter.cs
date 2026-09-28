using OnlineVideoManager.Core.Settings;

namespace OnlineVideoManager.Core.Links;

/// <summary>Decides whether a link picked up from the clipboard is worth offering for download.</summary>
public sealed class LinkFilter(LinkFilterMode mode, IEnumerable<string> extraSites)
{
    private readonly string[] _sites = [.. KnownVideoSites.Domains, .. extraSites.Where(s => !string.IsNullOrWhiteSpace(s))];

    public static LinkFilter From(AppSettings settings) => new(settings.LinkFilter, settings.ExtraSites);

    public bool Qualifies(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        if (mode == LinkFilterMode.AnyLink)
        {
            return true;
        }

        if (!_sites.Any(domain => KnownVideoSites.HostMatches(uri.Host, domain)))
        {
            return false;
        }

        return !IsYouTubeHost(uri.Host) || IsYouTubeContent(uri);
    }

    public IReadOnlyList<string> Apply(IEnumerable<string> urls) => urls.Where(Qualifies).ToList();

    private static bool IsYouTubeHost(string host) =>
        KnownVideoSites.HostMatches(host, "youtube.com") || KnownVideoSites.HostMatches(host, "youtu.be");

    /// <summary>YouTube links are everywhere (home page, search, sign-in); only keep ones that point
    /// at something downloadable.</summary>
    private static bool IsYouTubeContent(Uri uri)
    {
        if (LinkKey.YouTubeVideoId(uri) is not null)
        {
            return true;
        }

        var first = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return first.StartsWith('@')
            || first.Equals("playlist", StringComparison.OrdinalIgnoreCase)
            || first.Equals("channel", StringComparison.OrdinalIgnoreCase)
            || first.Equals("c", StringComparison.OrdinalIgnoreCase)
            || first.Equals("user", StringComparison.OrdinalIgnoreCase);
    }
}
