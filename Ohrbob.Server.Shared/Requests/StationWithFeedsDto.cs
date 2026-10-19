using System.Collections.Generic;
using System.Linq;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Server.Shared.Requests;

public class StationWithFeedsDto
{
    public Station Station { get; set; } = new Station();
    public List<FeedStatsDto> Feeds { get; set; } = new List<FeedStatsDto>();

    public bool IsUpdating => Feeds.Any(f => !f.LastUpdated.HasValue);

    /// <summary>
    /// Number of episodes still available to play for a specific profile (i.e. neither marked
    /// completed nor aged out). Only populated when a <c>profileId</c> is supplied; otherwise
    /// <c>null</c> (completion is per-profile, so it cannot be derived for an account alone).
    /// </summary>
    public long? UnplayedEpisodeCount { get; set; }
}
