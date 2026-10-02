using System;

namespace Ohrbob.Server.Shared.Data;

public class Episode : DbEntry
{
    public string Guid { get; set; } = string.Empty;
    public string FeedUrl { get; set; } = string.Empty; 
    public string FeedId { get; set; } = string.Empty; 
    public string Title { get; set; } = string.Empty; 
    public string Description { get; set; } = string.Empty;
    public string AudioUrl { get; set; } = string.Empty;
    public DateTime? PublishedAt { get; set; }
}