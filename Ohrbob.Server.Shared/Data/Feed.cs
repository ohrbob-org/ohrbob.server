using System;

namespace Ohrbob.Server.Shared.Data;

public class Feed : DbEntry
{
    public string Url { get; set; } = string.Empty;
}