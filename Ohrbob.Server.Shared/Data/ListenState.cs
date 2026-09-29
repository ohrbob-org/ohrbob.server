namespace Ohrbob.Server.Shared.Data;

public class ListenState
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string EpisodeId { get; set; } = "";
    public string StationId { get; set; } = "";
    public bool IsListened { get; set; }
    public int PositionSeconds { get; set; }
}