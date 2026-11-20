using System;

namespace Ohrbob.Server.Shared.Data;

public class Feed : DbEntry
{
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string[] Keywords { get; set; } = Array.Empty<string>();
    public DateTime? LastUpdated { get; set; }

    /// <summary>
    /// The RFC 5545 rule from the Podcasting 2.0
    /// <c>&lt;podcast:updateFrequency rrule="..."&gt;</c> element, when the feed declares one.
    /// Stored so the refresh schedule can be recomputed without re-reading the feed.
    /// </summary>
    public string? UpdateFrequencyRrule { get; set; }

    /// <summary>
    /// When the feed should next be polled, deduced from <see cref="UpdateFrequencyRrule"/>
    /// or the publish dates of its episodes. <c>null</c> means "use the hourly fallback".
    /// </summary>
    public DateTime? NextUpdateAt { get; set; }
}
