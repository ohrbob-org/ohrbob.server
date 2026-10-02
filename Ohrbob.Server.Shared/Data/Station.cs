using System;

namespace Ohrbob.Server.Shared.Data;

public enum PlaybackStrategy
{
    Random,
    Chronological,
    ChronologicalRoundRobin
}

public class Station : DbEntry
{
    public string AccountId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string[] Feeds { get; set; } = Array.Empty<string>();
    
    public PlaybackStrategy Strategy { get; set; } = PlaybackStrategy.Chronological;

    public bool IsPublic { get; set; }
}