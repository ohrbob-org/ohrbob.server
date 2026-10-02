using System;

namespace Ohrbob.Server.Shared.Data;

#nullable enable
public class Profile : DbEntry
{

    public string AccountId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty; 
    public string StreamToken { get; set; } = string.Empty;
    public string[] AllowedStationIds { get; set; } = Array.Empty<string>();
    
    public string? LastActiveStationId { get; set; }
    public string? LastActiveEpisodeId { get; set; }
}
#nullable disable