using System;

namespace Ohrbob.Server.Shared.Data;

public class Profile
{

    public string Id { get; set; } = ""; 
    public string AccountId { get; set; } = "";
    public string Name { get; set; } = ""; 
    public string StreamToken { get; set; } = "";
    public string[] AllowedStationIds { get; set; } = Array.Empty<string>();
}