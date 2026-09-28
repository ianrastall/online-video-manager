using OnlineVideoManager.Core.Links;
using OnlineVideoManager.Core.Settings;

namespace OnlineVideoManager.Tests;

public class LinkTests
{
    [Fact]
    public void Extracts_links_from_mixed_text_and_trims_punctuation()
    {
        var text = """
            Watch this: https://youtu.be/IQb6su5v5xA, and (https://vimeo.com/123456).
            "https://www.youtube.com/watch?v=hcoZX8f_yp0&t=10s"
            www.dailymotion.com/video/x8abc
            https://en.wikipedia.org/wiki/Foo_(bar)
            not a link: ftp://example.com
            """;

        var links = LinkExtractor.FromText(text);

        Assert.Equal(
            [
                "https://youtu.be/IQb6su5v5xA",
                "https://vimeo.com/123456",
                "https://www.youtube.com/watch?v=hcoZX8f_yp0&t=10s",
                "https://www.dailymotion.com/video/x8abc",
                "https://en.wikipedia.org/wiki/Foo_(bar)",
            ],
            links);
    }

    [Fact]
    public void Same_youtube_video_in_different_forms_counts_once()
    {
        var links = LinkExtractor.FromText("""
            https://youtu.be/IQb6su5v5xA?si=abc
            https://www.youtube.com/watch?v=IQb6su5v5xA
            https://m.youtube.com/shorts/IQb6su5v5xA
            https://youtu.be/R-E0HvHfa9o
            """);

        Assert.Equal(["https://youtu.be/IQb6su5v5xA?si=abc", "https://youtu.be/R-E0HvHfa9o"], links);
    }

    [Fact]
    public void Extracts_hrefs_from_clipboard_html()
    {
        var html = """
            Version:0.9
            StartHTML:0000000105
            <html><body><!--StartFragment--><a href="https://www.youtube.com/watch?v=WNnfTTbrANY&amp;list=PL1">one</a>
            <a href='/relative'>skip</a> <a href="https://vimeo.com/42">two</a><!--EndFragment--></body></html>
            """;

        Assert.Equal(["https://www.youtube.com/watch?v=WNnfTTbrANY&list=PL1", "https://vimeo.com/42"], LinkExtractor.FromHtml(html));
    }

    [Theory]
    [InlineData("https://youtu.be/IQb6su5v5xA", true)]
    [InlineData("https://www.youtube.com/watch?v=IQb6su5v5xA", true)]
    [InlineData("https://music.youtube.com/watch?v=IQb6su5v5xA", true)]
    [InlineData("https://www.youtube.com/@SomeChannel", true)]
    [InlineData("https://www.youtube.com/playlist?list=PL123", true)]
    [InlineData("https://www.youtube.com/", false)]
    [InlineData("https://www.youtube.com/results?search_query=cats", false)]
    [InlineData("https://vimeo.com/123", true)]
    [InlineData("https://old.reddit.com/r/videos/comments/abc", true)]
    [InlineData("https://example.com/video.mp4", false)]
    [InlineData("https://notyoutube.com/watch?v=IQb6su5v5xA", false)]
    public void Known_sites_filter(string url, bool expected)
    {
        Assert.Equal(expected, new LinkFilter(LinkFilterMode.KnownSites, []).Qualifies(url));
    }

    [Fact]
    public void Extra_sites_and_any_link_mode_widen_the_filter()
    {
        Assert.True(new LinkFilter(LinkFilterMode.KnownSites, ["example.com"]).Qualifies("https://media.example.com/v/1"));
        Assert.True(new LinkFilter(LinkFilterMode.AnyLink, []).Qualifies("https://anything.org/x"));
        Assert.False(new LinkFilter(LinkFilterMode.AnyLink, []).Qualifies("mailto:someone@example.com"));
    }
}
