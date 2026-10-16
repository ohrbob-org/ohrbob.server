using MongoDB.Driver;

namespace Ohrbob.Api.Migrations;

/// <summary>
/// A single, ordered, idempotent database migration.
/// </summary>
public interface IMigration
{
    /// <summary>Schema version this migration brings the database up to, e.g. "0.0.8".</summary>
    string Version { get; }

    Task ApplyAsync(IMongoDatabase database, CancellationToken cancellationToken = default);
}
