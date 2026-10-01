using System.Collections.Generic;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Server.Shared.Requests;

public class StationWithFeedsDto
{
    public Station Station { get; set; } = new Station();
    public List<FeedStatsDto> Feeds { get; set; } = new List<FeedStatsDto>();
}
