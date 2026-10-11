using System.Text.RegularExpressions;

namespace Ohrbob.Web.Extensions;

public static class StringExtensions
{
    public static string StripHtml(this string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        // This regex matches any HTML tags
        return Regex.Replace(input, "<.*?>", string.Empty);
    }
}