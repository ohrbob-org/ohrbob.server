using System;

namespace Ohrbob.Server.Shared.Data;

public class Station : DbEntry
{
    public string AccountId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string[] Feeds { get; set; } = Array.Empty<string>();
    
    public StationSchedule Schedule { get; set; } = new StationSchedule();

    public bool IsPublic { get; set; }
}