using FinMate.Application.MarketData;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.MarketData;

public class WebContentTests
{
    [Fact]
    public void ReadsRssWithCdataAndDates()
    {
        var items = FeedReader.Read("""
            <?xml version="1.0"?><rss version="2.0"><channel>
            <item><title><![CDATA[Giá xăng tăng mạnh]]></title><link>https://example.vn/a.html</link>
            <description><![CDATA[<p>Mô tả &amp; thêm</p>]]></description><pubDate>Thu, 05 Nov 2026 08:00:00 +0700</pubDate></item>
            <item><title>Không có link</title></item>
            <item><title>Link thường</title><link>http://example.vn/b.html</link></item>
            </channel></rss>
            """);

        var item = items.Should().ContainSingle("thiếu link hoặc không phải https thì bỏ").Subject;
        item.Title.Should().Be("Giá xăng tăng mạnh");
        item.Summary.Should().Be("Mô tả & thêm");
        item.PublishedAt.Should().Be(new DateTimeOffset(2026, 11, 5, 1, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void ReadsAtom()
    {
        var items = FeedReader.Read("""
            <feed xmlns="http://www.w3.org/2005/Atom"><entry><title>Giá điện</title>
            <link rel="self" href="https://example.vn/self"/><link rel="alternate" href="https://example.vn/dien"/>
            <published>2026-11-05T01:00:00Z</published></entry></feed>
            """);

        items.Should().ContainSingle().Which.Url.Should().Be("https://example.vn/dien");
    }

    [Theory]
    [InlineData("""<!DOCTYPE rss [<!ENTITY x SYSTEM "file:///etc/passwd">]><rss><channel><item><title>&x;</title><link>https://a.vn/b</link></item></channel></rss>""")]
    [InlineData("""<!DOCTYPE lolz [<!ENTITY a "aaaa"><!ENTITY b "&a;&a;">]><rss><channel/></rss>""")]
    [InlineData("không phải xml")]
    public void AFeedWithADtdOrGarbageReadsAsEmpty(string content)
    {
        FeedReader.LooksLikeFeed(content).Should().BeFalse();
        FeedReader.Read(content).Should().BeEmpty();
    }

    [Fact]
    public void ArticleTextDropsScriptsMenusAndKeepsTheBody()
    {
        var body = string.Join(" ", Enumerable.Repeat("Giá xăng tăng 500 đồng mỗi lít từ chiều nay.", 10));
        var html = $"""
            <html><head><title>Tiêu đề &amp; trang</title><script>var secret = "x";</script></head>
            <body><nav>Menu chính</nav><article><h1>Giá xăng</h1><p>{body}</p><script>track()</script></article><footer>Liên hệ</footer></body></html>
            """;

        var text = HtmlTextExtractor.ArticleText(html);

        text.Should().Contain("Giá xăng tăng 500 đồng");
        text.Should().NotContain("Menu chính").And.NotContain("secret").And.NotContain("track").And.NotContain("Liên hệ");
        HtmlTextExtractor.Title(html).Should().Be("Tiêu đề & trang");
    }

    [Fact]
    public void ARelatedStoryCardIsNotMistakenForTheArticle()
    {
        var card = string.Join(" ", Enumerable.Repeat("Tin liên quan về giá xăng hôm qua.", 8));
        var body = string.Join(" ", Enumerable.Repeat("Giá điện tăng từ tháng sau theo quyết định mới.", 30));

        var cardFirst = HtmlTextExtractor.ArticleText($"<html><body><article>{card}</article><article>{body}</article></body></html>");
        var outsideArticle = HtmlTextExtractor.ArticleText($"<html><body><article>{card}</article><div class=\"detail\">{body}</div></body></html>");

        cardFirst.Should().Be(body);
        outsideArticle.Should().Contain("Giá điện tăng từ tháng sau");
    }

    [Theory]
    [InlineData("""<meta property="article:published_time" content="2026-11-05T08:00:00+07:00">""")]
    [InlineData("""<meta content="2026-11-05T08:00:00+07:00" name="pubdate">""")]
    [InlineData("""<script type="application/ld+json">{"datePublished":"2026-11-05T08:00:00+07:00"}</script>""")]
    [InlineData("""<time datetime="2026-11-05T08:00:00+07:00">hôm nay</time>""")]
    public void ReadsThePublishedDate(string html)
        => HtmlTextExtractor.PublishedAt(html).Should().Be(new DateTimeOffset(2026, 11, 5, 1, 0, 0, TimeSpan.Zero));

    [Fact]
    public void NoDateOrABadDateIsNull()
    {
        HtmlTextExtractor.PublishedAt("<p>không có ngày</p>").Should().BeNull();
        HtmlTextExtractor.PublishedAt("""<time datetime="hôm qua">x</time>""").Should().BeNull();
    }

    [Fact]
    public void ControlCharactersThatPostgresRefusesAreRemoved()
        => HtmlTextExtractor.PageText("<p>Giá&#0; xăng</p>").Should().NotContain("\0");

    [Fact]
    public void AnEmptyAppShellHasNoContent()
        => HtmlTextExtractor.ArticleText("<html><body><div id=\"root\"></div><script src=\"/app.js\"></script></body></html>").Length
            .Should().BeLessThan(HtmlTextExtractor.MinArticleLength);

    [Fact]
    public void LinksAreAbsoluteHttpsOnlyWithoutFragments()
    {
        var links = HtmlTextExtractor.Links(
            """<a href="/gia-xang.html#top">Giá <b>xăng</b></a> <a href="javascript:evil()">x</a> <a href="http://a.vn/b">y</a> <script>"<a href='https://t.vn/z'>z</a>"</script>""",
            new Uri("https://example.vn/tin/"));

        links.Should().ContainSingle().Which.Should().Be(new PageLink("https://example.vn/gia-xang.html", "Giá xăng"));
    }

    [Fact]
    public void AHostilePageCannotStallTheExtractor()
    {
        var hostile = string.Concat(Enumerable.Repeat("<a href=\"", 50_000)) + string.Concat(Enumerable.Repeat("<script>", 50_000));

        var action = () =>
        {
            HtmlTextExtractor.ArticleText(hostile);
            HtmlTextExtractor.Links(hostile, new Uri("https://example.vn/"));
        };

        action.ExecutionTime().Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData("Giá xăng RON 95 tăng mạnh", true)]
    [InlineData("GIÁ ĐIỆN sẽ tăng từ tháng sau", true)]
    [InlineData("Lạm phát tháng 10", true)]
    [InlineData("Đội tuyển bóng đá thắng đậm", false)]
    public void TopicFilterKeepsOnlyPriceNews(string title, bool expected)
        => MarketTopics.IsRelevant(title).Should().Be(expected);

    [Fact]
    public void RobotsLongestMatchWinsAndUnknownAgentsFallBackToStar()
    {
        var rules = RobotsRules.Parse("""
            User-agent: *
            Disallow: /private/
            Allow: /private/public.html
            Disallow: /*.pdf$
            """);

        rules.IsAllowed("/tin/a.html").Should().BeTrue();
        rules.IsAllowed("/private/x").Should().BeFalse();
        rules.IsAllowed("/private/public.html").Should().BeTrue();
        rules.IsAllowed("/report.pdf").Should().BeFalse();
        rules.IsAllowed("/report.pdf.html").Should().BeTrue();
    }

    [Fact]
    public void RobotsNamingOurBotTakesPriorityOverStar()
    {
        var rules = RobotsRules.Parse("""
            User-agent: FinMateBot
            Disallow: /

            User-agent: *
            Allow: /
            """);

        rules.IsAllowed("/a").Should().BeFalse();
    }

    [Fact]
    public void AgentsSharingAGroupAllGetItsRules()
    {
        var rules = RobotsRules.Parse("""
            User-agent: GPTBot
            User-agent: ClaudeBot
            Disallow: /

            User-agent: *
            Disallow:
            """);

        rules.AiAllowed.Should().BeFalse("trang chặn bot AI theo tên");
        rules.IsAllowed("/a").Should().BeTrue("nhưng không chặn công cụ đọc thường");
    }

    [Theory]
    [InlineData("User-agent: *\nContent-Signal: search=yes, ai-input=no\nAllow: /", false)]
    [InlineData("User-agent: *\nContent-Signal: search=yes,ai-train=no,ai-input=yes\nAllow: /", true)]
    [InlineData("User-agent: *\nDisallow:", true)]
    public void ContentSignalControlsAiUse(string robots, bool expected)
        => RobotsRules.Parse(robots).AiAllowed.Should().Be(expected);

    [Fact]
    public void DenyAllForbidsEverything()
    {
        RobotsRules.DenyAll.IsAllowed("/").Should().BeFalse();
        RobotsRules.DenyAll.AiAllowed.Should().BeFalse();
        RobotsRules.AllowAll.IsAllowed("/").Should().BeTrue();
    }
}
