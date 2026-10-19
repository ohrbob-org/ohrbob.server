using System.Net;
using System.Text.RegularExpressions;

namespace Ohrbob.Server.Shared.Text;

/// <summary>
/// Converts feed-provided HTML fragments (episode and feed descriptions) into plain text.
///
/// Feed descriptions are commonly HTML and are frequently entity-encoded inside a CDATA
/// section (e.g. <c>R&amp;#252;cktritt</c>). CDATA is not entity-decoded by the XML parser,
/// so those entities survive parsing and must be decoded here before the text is displayed.
/// </summary>
public static class HtmlText
{
    private static readonly Regex TagRegex = new("<.*?>", RegexOptions.Compiled);

    /// <summary>
    /// Strips HTML tags and decodes HTML entities, returning plain text suitable for display.
    /// </summary>
    public static string ToPlainText(string html)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;

        // Strip tags first so that escaped markup (e.g. "&lt;b&gt;") survives as literal
        // text instead of being mistaken for a real tag, then decode the HTML entities.
        var withoutTags = TagRegex.Replace(html, string.Empty);
        return WebUtility.HtmlDecode(withoutTags).Trim();
    }
}
