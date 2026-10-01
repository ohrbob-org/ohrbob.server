using System;

namespace Ohrbob.Server.Shared.Data;

public class Feed : DbEntry
{
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public DateTime? LastUpdated { get; set; }
}
