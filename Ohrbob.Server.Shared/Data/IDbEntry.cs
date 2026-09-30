using System;

namespace Ohrbob.Server.Shared.Data;

public abstract class DbEntry
{
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Id { get; set; } = string.Empty;
}