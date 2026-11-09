namespace Ohrbob.Web.Models;

public static class MorebobThanks
{
    private static readonly string[] Messages =
    [
        "Thanks for keeping the music flowing — Morebob makes your stations truly yours.",
        "You're the reason ohrbob keeps getting better. Thank you for going Morebob.",
        "Thank you for supporting independent listening — enjoy every station without limits.",
        "Morebob exists because of listeners like you. Thanks for being here.",
        "Thanks for believing in ohrbob. Your support keeps the feeds coming.",
        "You've unlocked the full ohrbob experience — thank you for making it possible.",
        "Thanks for being part of the Morebob crew. Your stations, your rules.",
        "Every extra profile, every feed, every listener — thank you for supporting ohrbob.",
        "You make ohrbob possible. Thanks for choosing Morebob.",
        "Thank you for listening your way — and for helping ohrbob grow."
    ];

    public static string For(string? accountId)
    {
        if (string.IsNullOrEmpty(accountId)) return Messages[0];

        // FNV-1a: stable across processes, unlike string.GetHashCode().
        uint hash = 2166136261;
        foreach (var c in accountId)
        {
            hash ^= c;
            hash *= 16777619;
        }

        return Messages[(int)(hash % (uint)Messages.Length)];
    }
}
