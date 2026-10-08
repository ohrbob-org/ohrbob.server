using System.Collections.Generic;

namespace Ohrbob.Server.Shared.Data;

public enum FeedSortOrder
{
    OldestToNewest,
    NewestToOldest
}

public class StationFeedSchedule
{
    public string Url { get; set; } = string.Empty;
    public FeedSortOrder SortOrder { get; set; } = FeedSortOrder.OldestToNewest;
}

public class StationSchedule
{
    public List<StationFeedSchedule> Feeds { get; set; } = new();
}
