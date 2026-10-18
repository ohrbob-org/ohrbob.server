using System.Xml.Linq;

namespace Ohrbob.Web.Services;

/// <summary>
/// Extracts feed URLs from an OPML subscription list (e.g. an AntennaPod export).
/// </summary>
public static class OpmlParser
{
    /// <summary>
    /// Reads every <c>&lt;outline&gt;</c> (including nested ones) that carries an
    /// <c>xmlUrl</c> and returns the URLs in document order, de-duplicated.
    /// </summary>
    /// <exception cref="InvalidDataException">The document root is not <c>opml</c>.</exception>
    /// <exception cref="System.Xml.XmlException">The document is not well-formed XML.</exception>
    public static List<string> ExtractFeedUrls(Stream stream)
    {
        var document = XDocument.Load(stream);
        var root = document.Root;
        if (root == null || !string.Equals(root.Name.LocalName, "opml", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The document is not an OPML file.");

        var urls = new List<string>();
        var seen = new HashSet<string>();

        foreach (var outline in root.Descendants())
        {
            if (!string.Equals(outline.Name.LocalName, "outline", StringComparison.OrdinalIgnoreCase))
                continue;

            var xmlUrlAttribute = outline.Attributes()
                .FirstOrDefault(a => string.Equals(a.Name.LocalName, "xmlUrl", StringComparison.OrdinalIgnoreCase));

            var xmlUrl = xmlUrlAttribute?.Value?.Trim();
            if (string.IsNullOrEmpty(xmlUrl)) continue;

            if (seen.Add(xmlUrl))
                urls.Add(xmlUrl);
        }

        return urls;
    }
}
