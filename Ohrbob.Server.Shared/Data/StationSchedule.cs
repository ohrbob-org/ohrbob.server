using System.Collections.Generic;

namespace Ohrbob.Server.Shared.Data;

public enum FeedSortOrder
{
    OldestToNewest,
    NewestToOldest,
    Randomized
}

public class StationFeedSchedule
{
    public string Url { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public FeedSortOrder SortOrder { get; set; } = FeedSortOrder.OldestToNewest;
    public int EpisodesToPlay { get; set; } = 1;
}

public class StationSchedule
{
    public List<StationFeedSchedule> Feeds { get; set; } = new();
}
