using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Server.Shared.Requests;

public class EpisodePlayStateDto
{
    public Episode Episode { get; set; } = new();
    public int PositionSeconds { get; set; }
    public bool IsActive { get; set; }
}
