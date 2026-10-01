using System;

namespace Ohrbob.Server.Shared.Requests;

public class FeedStatsDto
{
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public DateTime? LastUpdated { get; set; }
    public long EpisodeCount { get; set; }
}
