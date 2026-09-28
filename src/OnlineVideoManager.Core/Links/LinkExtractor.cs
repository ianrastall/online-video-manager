using System.Net;
using System.Text.RegularExpressions;

namespace OnlineVideoManager.Core.Links;

/// <summary>Pulls http(s) links out of free text and clipboard HTML.</summary>
public static partial class LinkExtractor
{
    // Stops at whitespace and characters that never appear unescaped in a URL but commonly
    // surround one in prose, Markdown or chat text.
    [GeneratedRegex(@"(?:https?://|www\.)[^\s""'<>`\[\]{}|\\^]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"href\s*=\s*(?:""(?<u>[^""]*)""|'(?<u>[^']*)')", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HrefPattern();

    /// <summary>All links in the text, in order, without duplicates (see <see cref="LinkKey"/>).</summary>
    public static IReadOnlyList<string> FromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var found = new List<string>();
        foreach (Match m in UrlPattern().Matches(text))
        {
            var url = Clean(m.Value);
            if (url is not null)
            {
                found.Add(url);
            }
        }

        return Distinct(found);
    }

    /// <summary>Links from <c>href</c> attributes of an HTML fragment (e.g. a copied part of a web page).</summary>
    public static IReadOnlyList<string> FromHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return [];
        }

        var found = new List<string>();
        foreach (Match m in HrefPattern().Matches(html))
        {
            var url = Clean(WebUtility.HtmlDecode(m.Groups["u"].Value));
            if (url is not null)
            {
                found.Add(url);
            }
        }

        return Distinct(found);
    }

    /// <summary>Links from both the plain text and the HTML flavour of one clipboard entry.</summary>
    public static IReadOnlyList<string> FromClipboard(string? text, string? html) =>
        Distinct([.. FromText(text), .. FromHtml(html)]);

    public static IReadOnlyList<string> Distinct(IEnumerable<string> urls)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var url in urls)
        {
            if (seen.Add(LinkKey.For(url)))
            {
                result.Add(url);
            }
        }

        return result;
    }

    /// <summary>Trim punctuation that belongs to the surrounding text and validate the result.</summary>
    internal static string? Clean(string candidate)
    {
        var url = candidate.Trim();
        while (url.Length > 0)
        {
            var last = url[^1];
            if (".,;:!?*'\"".Contains(last))
            {
                url = url[..^1];
            }
            else if (last == ')' && url.Count(c => c == ')') > url.Count(c => c == '('))
            {
                // "(see https://example.com/x)" - but keep balanced parentheses as in Wikipedia URLs.
                url = url[..^1];
            }
            else
            {
                break;
            }
        }

        if (url.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host)
            || !uri.Host.Contains('.'))
        {
            return null;
        }

        return url;
    }
}
