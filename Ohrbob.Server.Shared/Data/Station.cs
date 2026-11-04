using System;

namespace Ohrbob.Server.Shared.Data;

public class Station : DbEntry
{
    public string AccountId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ShareCode { get; set; } = string.Empty;
    public string[] Feeds { get; set; } = Array.Empty<string>();
    
    public StationSchedule Schedule { get; set; } = new StationSchedule();

    public bool IsPublic { get; set; }
    public bool IsAging { get; set; }

    /// <summary>When enabled, playback restarts at the first episode after the last one, forever.</summary>
    public bool EndlessSpin { get; set; }

    /// <summary>When enabled, subscribers may only play episodes the host profile has completed.</summary>
    public bool HostPaceEnabled { get; set; }

    /// <summary>The owner profile whose completed episodes gate subscriber playback.</summary>
    public string? HostProfileId { get; set; }
}