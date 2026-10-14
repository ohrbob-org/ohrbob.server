using System;

namespace Ohrbob.Server.Shared.Data;

public class ApiKey : DbEntry
{
    public string AccountId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
}
