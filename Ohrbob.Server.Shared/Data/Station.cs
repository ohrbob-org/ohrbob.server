using System;

namespace Ohrbob.Server.Shared.Data;

public class Station
{
    public string Id { get; set; } = "";
    public string AccountId { get; set; } = "";
    public string Name { get; set; } = ""; 
    public string[] Feeds { get; set; } = Array.Empty<string>();
}