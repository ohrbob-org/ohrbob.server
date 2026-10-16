using System;

namespace Ohrbob.Server.Shared.Data;

/// <summary>
/// Stores the current database schema version. A single document in the
/// DbVersionCollection holds the version that has last been applied.
/// </summary>
public class DbVersion : DbEntry
{
    public string Version { get; set; } = string.Empty;
}
