using System;

namespace Ohrbob.Server.Shared.Requests;

/// <summary>Result of a manual feed refresh, rendered back into the station editor.</summary>
public class FeedRefreshResultDto
{
    public string Id { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public long EpisodeCount { get; set; }
    public DateTime? LastUpdated { get; set; }
    public DateTime? NextUpdateAt { get; set; }
}
