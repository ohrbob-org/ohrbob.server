using System.Text.RegularExpressions;

namespace Ohrbob.Server.Shared.Data;

#nullable enable
public static class ProfileColors
{
    public static readonly string[] Palette =
    {
        "#ef4444", "#f97316", "#f59e0b", "#ca8a04", "#65a30d",
        "#16a34a", "#059669", "#0d9488", "#0891b2", "#0284c7",
        "#3b82f6", "#6366f1", "#8b5cf6", "#a855f7", "#d946ef",
        "#ec4899", "#f43f5e", "#78716c", "#64748b", "#475569"
    };

    private static readonly Regex HexColor = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

    public static bool IsValid(string? color) => string.IsNullOrEmpty(color) || HexColor.IsMatch(color);

    public static string Resolve(Profile? profile)
    {
        if (profile != null && !string.IsNullOrEmpty(profile.Color) && IsValid(profile.Color))
        {
            return profile.Color;
        }

        var key = profile?.Id ?? string.Empty;
        return Palette[(key.GetHashCode() & int.MaxValue) % Palette.Length];
    }
}
