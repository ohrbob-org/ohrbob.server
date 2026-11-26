using System.Globalization;

namespace Ohrbob.Web;

/// <summary>
/// Picks white or navy ink for a profile colour so the initial stays readable on
/// both the pale and the deep palette swatches.
/// </summary>
public static class ProfileColorInk
{
    public static string For(string? color)
    {
        var value = color?.TrimStart('#');
        if (string.IsNullOrEmpty(value) || value.Length != 6 ||
            !int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return "var(--metro-on-accent)";
        }

        var r = (rgb >> 16) & 0xFF;
        var g = (rgb >> 8) & 0xFF;
        var b = rgb & 0xFF;
        var luma = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
        return luma > 0.55 ? "var(--metro-navy)" : "var(--metro-on-accent)";
    }
}
