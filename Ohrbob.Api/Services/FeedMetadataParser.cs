using System.Xml.Linq;

namespace Ohrbob.Api.Services;

public static class FeedMetadataParser
{
    private static readonly XNamespace Itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
    private static readonly XNamespace Podcast = "https://podcastindex.org/namespace/1.0";

    /// <summary>Returns the first (top-level) iTunes category name, e.g. "True Crime".</summary>
    public static string ParseCategory(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return string.Empty;

        try
        {
            var xDoc = XDocument.Parse(xml);
            var category = xDoc.Descendants(Itunes + "category").FirstOrDefault();
            return category?.Attribute("text")?.Value.Trim() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Returns the RFC 5545 rule from the Podcasting 2.0
    /// <c>&lt;podcast:updateFrequency rrule="..."&gt;</c> element, or <c>null</c> when the feed
    /// does not declare one. This is the publisher's own statement of how often new
    /// episodes appear, so it is preferred over any pattern guessed from publish dates.
    /// </summary>
    public static string? ParseUpdateFrequencyRrule(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;

        try
        {
            var xDoc = XDocument.Parse(xml);
            var rrule = xDoc.Descendants(Podcast + "updateFrequency")
                .Select(e => e.Attribute("rrule")?.Value?.Trim())
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            return rrule;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Returns the cleaned <c>itunes:keywords</c> list, or <c>null</c> when absent.</summary>
    public static string[]? ParseKeywords(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;

        try
        {
            var xDoc = XDocument.Parse(xml);
            var keywordsElement = xDoc.Descendants(Itunes + "keywords").FirstOrDefault();
            if (keywordsElement == null || string.IsNullOrWhiteSpace(keywordsElement.Value))
                return null;

            var keywords = keywordsElement.Value
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim())
                .Where(k => !string.IsNullOrEmpty(k))
                .ToArray();
            return keywords.Length > 0 ? keywords : null;
        }
        catch
        {
            return null;
        }
    }
}
