using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace FinMate.Application.MarketData;

public sealed record FeedItem(string Url, string Title, string? Summary, DateTimeOffset? PublishedAt);

/// <summary>Đọc RSS và Atom. Cấm DTD vì nội dung đến từ trang lạ: DTD mở đường cho XXE và bom thực thể.</summary>
public static class FeedReader
{
    private const string Atom = "{http://www.w3.org/2005/Atom}";

    public static bool LooksLikeFeed(string content)
    {
        var root = RootName(content);
        return root is "rss" or "feed" or "RDF";
    }

    public static IReadOnlyList<FeedItem> Read(string content)
    {
        var document = Parse(content);
        if (document?.Root is null)
        {
            return [];
        }

        var entries = document.Descendants("item").Concat(document.Descendants($"{Atom}entry"));
        var items = new List<FeedItem>();
        foreach (var entry in entries)
        {
            var title = Text(entry, "title") ?? Text(entry, $"{Atom}title");
            var url = Text(entry, "link") ?? AtomLink(entry);
            if (string.IsNullOrWhiteSpace(title)
                || !Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps)
            {
                continue;
            }

            var summary = Text(entry, "description") ?? Text(entry, $"{Atom}summary");
            items.Add(new FeedItem(
                uri.AbsoluteUri,
                HtmlTextExtractor.PageText(title),
                summary is null ? null : HtmlTextExtractor.PageText(summary),
                Date(Text(entry, "pubDate") ?? Text(entry, $"{Atom}published") ?? Text(entry, $"{Atom}updated"))));
        }

        return items;
    }

    private static string? RootName(string content) => Parse(content)?.Root?.Name.LocalName;

    private static XDocument? Parse(string content)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersFromEntities = 0 };
        try
        {
            using var stringReader = new StringReader(content.TrimStart('﻿', ' ', '\r', '\n', '\t'));
            using var reader = XmlReader.Create(stringReader, settings);
            return XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static string? Text(XElement entry, XName name) => entry.Element(name)?.Value;

    private static string? AtomLink(XElement entry)
    {
        var links = entry.Elements($"{Atom}link").ToList();
        var chosen = links.FirstOrDefault(l => l.Attribute("rel")?.Value is null or "alternate");
        return chosen?.Attribute("href")?.Value;
    }

    private static DateTimeOffset? Date(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date.ToUniversalTime() : null;
}
