using System;

namespace Ohrbob.Server.Shared.Data;

public class Episode : DbEntry
{
    public string Guid { get; set; } = string.Empty;
    public string FeedUrl { get; set; } = string.Empty; 
    public string FeedId { get; set; } = string.Empty; 
    public string Title { get; set; } = string.Empty; 
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Plain-text rendering of <see cref="Description"/> (HTML tags stripped, HTML entities
    /// decoded). Feed descriptions are HTML and are often entity-encoded inside CDATA, so
    /// this is what the UI should display.
    /// </summary>
    public string PlainDescription { get; set; } = string.Empty;
    public string AudioUrl { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public DateTime? PublishedAt { get; set; }
}