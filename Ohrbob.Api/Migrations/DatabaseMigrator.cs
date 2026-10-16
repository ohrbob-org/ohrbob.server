using MongoDB.Driver;
using Ohrbob.Api.Configuration;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Migrations;

public sealed record MigrationResult(string FromVersion, string ToVersion, IReadOnlyList<string> AppliedVersions);

/// <summary>
/// Runs all pending <see cref="IMigration"/>s at application startup, in ascending
/// version order, and records the resulting schema version in the database so the
/// version can be deduced purely from the data store on the next boot.
/// </summary>
public class DatabaseMigrator
{
    public const string VersionCollectionName = "DbVersionCollection";

    /// <summary>Version assumed for databases created before version tracking existed.</summary>
    public const string BaselineVersion = "0.0.7";

    private const string VersionDocumentId = "schema";

    private readonly MongoDbContext _dbContext;
    private readonly IReadOnlyList<IMigration> _migrations;
    private readonly ILogger<DatabaseMigrator> _logger;

    public DatabaseMigrator(MongoDbContext dbContext, IEnumerable<IMigration> migrations, ILogger<DatabaseMigrator> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
        _migrations = migrations.OrderBy(m => ParseVersion(m.Version)).ToList();
    }

    public async Task<MigrationResult> MigrateAsync(CancellationToken cancellationToken = default)
    {
        var database = _dbContext.GetDatabase()
            ?? throw new InvalidOperationException("MongoDB database is not available.");

        var versionCollection = database.GetCollection<DbVersion>(VersionCollectionName);
        var stored = await versionCollection.Find(FilterDefinition<DbVersion>.Empty).FirstOrDefaultAsync(cancellationToken);

        var fromVersion = string.IsNullOrWhiteSpace(stored?.Version) ? BaselineVersion : stored!.Version;
        var targetVersion = _migrations.Count > 0 ? _migrations[^1].Version : BaselineVersion;

        var from = ParseVersion(fromVersion);
        var applied = new List<string>();

        foreach (var migration in _migrations)
        {
            if (ParseVersion(migration.Version) <= from)
                continue;

            _logger.LogInformation("Applying database migration {Version}", migration.Version);
            await migration.ApplyAsync(database, cancellationToken);
            await StoreVersionAsync(versionCollection, migration.Version, cancellationToken);
            applied.Add(migration.Version);
            _logger.LogInformation("Applied database migration {Version}", migration.Version);
        }

        return new MigrationResult(fromVersion, targetVersion, applied);
    }

    private static async Task StoreVersionAsync(IMongoCollection<DbVersion> collection, string version, CancellationToken cancellationToken)
    {
        var filter = Builders<DbVersion>.Filter.Eq(x => x.Id, VersionDocumentId);
        var update = Builders<DbVersion>.Update
            .Set(x => x.Version, version)
            .SetOnInsert(x => x.CreatedAt, DateTime.Now);

        await collection.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true }, cancellationToken);
    }

    private static Version ParseVersion(string version) =>
        Version.TryParse(version, out var parsed) ? parsed : new Version(0, 0, 0);
}
