using System.Xml.Linq;

namespace Ohrbob.Api.Services;

public static class FeedMetadataParser
{
    private static readonly XNamespace Itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";

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
}
