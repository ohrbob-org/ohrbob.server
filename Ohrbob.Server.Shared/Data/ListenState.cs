using System;

namespace Ohrbob.Server.Shared.Data;

public class ListenState : DbEntry
{
    public string UserId { get; set; } = string.Empty;
    public string EpisodeId { get; set; } = string.Empty;
    public string StationId { get; set; } = string.Empty;
    public bool IsListened { get; set; }
    public int PositionSeconds { get; set; }
}