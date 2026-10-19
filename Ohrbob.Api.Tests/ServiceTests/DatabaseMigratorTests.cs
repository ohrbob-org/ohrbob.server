using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Ohrbob.Api.Configuration;
using Ohrbob.Api.Migrations;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Tests.ServiceTests;

public class DatabaseMigratorTests
{
    private const string DatabaseName = "TestsOhrbobMigrations";

    private MongoDbContext _dbContext = null!;

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(ApiTestApp<Program>.MongodbConnectionStringEnvVariable)
        ?? "mongodb://localhost:27017";

    [SetUp]
    public void Setup()
    {
        _dbContext = new MongoDbContext(ConnectionString, DatabaseName);
        DropDatabase();
    }

    [TearDown]
    public void TearDown()
    {
        DropDatabase();
    }

    private void DropDatabase()
    {
        _dbContext.GetDatabase()!.RunCommand(new JsonCommand<BsonDocument>("{ dropDatabase: 1 }"));
    }

    private DatabaseMigrator CreateMigrator(params IMigration[] migrations)
    {
        return new DatabaseMigrator(_dbContext, migrations, NullLogger<DatabaseMigrator>.Instance);
    }

    private async Task<DbVersion?> ReadStoredVersionAsync()
    {
        var collection = _dbContext.GetCollection<DbVersion>(DatabaseMigrator.VersionCollectionName);
        return await collection.Find(FilterDefinition<DbVersion>.Empty).FirstOrDefaultAsync();
    }

    [Test]
    public async Task MigrateAsync_OnFreshDatabase_AppliesMigrationsAndStoresVersion()
    {
        var migration = new RecordingMigration("0.0.8");
        var migrator = CreateMigrator(migration);

        var result = await migrator.MigrateAsync();

        Assert.That(result.FromVersion, Is.EqualTo("0.0.7"));
        Assert.That(result.ToVersion, Is.EqualTo("0.0.8"));
        Assert.That(result.AppliedVersions, Is.EqualTo(new[] { "0.0.8" }));
        Assert.That(migration.ApplyCount, Is.EqualTo(1));

        var stored = await ReadStoredVersionAsync();
        Assert.That(stored, Is.Not.Null);
        Assert.That(stored!.Version, Is.EqualTo("0.0.8"));
    }

    [Test]
    public async Task MigrateAsync_WhenAlreadyAtCurrentVersion_DoesNotReapply()
    {
        await SeedStoredVersionAsync("0.0.8");
        var migration = new RecordingMigration("0.0.8");
        var migrator = CreateMigrator(migration);

        var result = await migrator.MigrateAsync();

        Assert.That(result.FromVersion, Is.EqualTo("0.0.8"));
        Assert.That(result.ToVersion, Is.EqualTo("0.0.8"));
        Assert.That(result.AppliedVersions, Is.Empty);
        Assert.That(migration.ApplyCount, Is.EqualTo(0));
    }

    [Test]
    public async Task MigrateAsync_WhenBehind_AppliesOnlyNewerMigrationsInOrder()
    {
        await SeedStoredVersionAsync("0.0.8");
        // Deliberately registered out of order to prove the runner sorts by version.
        var newer = new RecordingMigration("0.0.9");
        var older = new RecordingMigration("0.0.8");
        var migrator = CreateMigrator(newer, older);

        var result = await migrator.MigrateAsync();

        Assert.That(result.FromVersion, Is.EqualTo("0.0.8"));
        Assert.That(result.ToVersion, Is.EqualTo("0.0.9"));
        Assert.That(result.AppliedVersions, Is.EqualTo(new[] { "0.0.9" }));
        Assert.That(newer.ApplyCount, Is.EqualTo(1));
        Assert.That(older.ApplyCount, Is.EqualTo(0));

        var stored = await ReadStoredVersionAsync();
        Assert.That(stored!.Version, Is.EqualTo("0.0.9"));
    }

    [Test]
    public async Task MigrateAsync_WithNoMigrations_KeepsBaselineVersion()
    {
        var migrator = CreateMigrator();

        var result = await migrator.MigrateAsync();

        Assert.That(result.ToVersion, Is.EqualTo("0.0.7"));
        Assert.That(result.AppliedVersions, Is.Empty);
    }

    [Test]
    public async Task AddIndexesMigration_CreatesHotPathIndexes()
    {
        var migrator = CreateMigrator(new Migration_0_0_8_AddIndexes());

        await migrator.MigrateAsync();

        var apiKeyIndexes = await GetIndexesAsync<ApiKey>("ApiKeyCollection");
        AssertUniqueIndex(apiKeyIndexes, "token_unique", "Token");
        AssertIndexExists(apiKeyIndexes, "accountId");

        var profileIndexes = await GetIndexesAsync<Profile>("ProfileCollection");
        AssertUniqueIndex(profileIndexes, "streamToken_unique", "StreamToken");
        AssertIndexExists(profileIndexes, "accountId");

        var episodeIndexes = await GetIndexesAsync<Episode>("EpisodeCollection");
        AssertIndexExists(episodeIndexes, "feedId_guid");
        AssertIndexExists(episodeIndexes, "feedUrl");

        var feedIndexes = await GetIndexesAsync<Feed>("FeedCollection");
        AssertIndexExists(feedIndexes, "url");

        var accountIndexes = await GetIndexesAsync<Account>("AccountCollection");
        AssertIndexExists(accountIndexes, "email");
        AssertIndexExists(accountIndexes, "identityProvider");

        var stationIndexes = await GetIndexesAsync<Station>("StationCollection");
        AssertIndexExists(stationIndexes, "accountId");

        var listenStateIndexes = await GetIndexesAsync<ListenState>("ListenStateCollection");
        AssertIndexExists(listenStateIndexes, "userId_episodeId");
        AssertIndexExists(listenStateIndexes, "userId_createdAt");
    }

    [Test]
    public async Task BackfillPlainDescriptionsMigration_DecodesStoredHtmlDescriptions()
    {
        var episodes = _dbContext.GetCollection<Episode>("EpisodeCollection");
        await episodes.InsertManyAsync(new[]
        {
            new Episode { Id = "e1", Guid = "e1", Description = "<p>R&#252;cktritt von B&#228;rbel</p>", PlainDescription = string.Empty },
            new Episode { Id = "e2", Guid = "e2", Description = "No entities here", PlainDescription = string.Empty },
            new Episode { Id = "e3", Guid = "e3", Description = string.Empty, PlainDescription = string.Empty }
        });

        var migrator = CreateMigrator(new Migration_0_0_9_BackfillPlainDescriptions());
        await migrator.MigrateAsync();

        var e1 = await episodes.Find(e => e.Id == "e1").FirstOrDefaultAsync();
        Assert.That(e1!.PlainDescription, Is.EqualTo("Rücktritt von Bärbel"));
        Assert.That(e1.Description, Is.EqualTo("<p>R&#252;cktritt von B&#228;rbel</p>"),
            "The raw stored description must be left untouched");

        var e2 = await episodes.Find(e => e.Id == "e2").FirstOrDefaultAsync();
        Assert.That(e2!.PlainDescription, Is.EqualTo("No entities here"));

        var e3 = await episodes.Find(e => e.Id == "e3").FirstOrDefaultAsync();
        Assert.That(e3!.PlainDescription, Is.EqualTo(string.Empty));
    }

    private async Task SeedStoredVersionAsync(string version)
    {
        var collection = _dbContext.GetCollection<DbVersion>(DatabaseMigrator.VersionCollectionName);
        await collection.InsertOneAsync(new DbVersion { Id = "schema", Version = version, CreatedAt = DateTime.Now });
    }

    private async Task<List<BsonDocument>> GetIndexesAsync<T>(string collectionName)
    {
        var collection = _dbContext.GetCollection<T>(collectionName);
        using var cursor = await collection.Indexes.ListAsync();
        return await cursor.ToListAsync();
    }

    private static void AssertIndexExists(List<BsonDocument> indexes, string name)
    {
        Assert.That(indexes.Any(i => i.Contains("name") && i["name"].AsString == name), Is.True,
            $"Expected index '{name}' to exist. Found: {string.Join(", ", indexes.Select(i => i["name"]))}");
    }

    private static void AssertUniqueIndex(List<BsonDocument> indexes, string name, string expectedField)
    {
        var index = indexes.FirstOrDefault(i => i.Contains("name") && i["name"].AsString == name);
        Assert.That(index, Is.Not.Null, $"Expected unique index '{name}' to exist.");
        Assert.That(index!["unique"].AsBoolean, Is.True, $"Expected index '{name}' to be unique.");
        Assert.That(index["key"].AsBsonDocument.Names, Does.Contain(expectedField));
    }

    private sealed class RecordingMigration : IMigration
    {
        public RecordingMigration(string version) => Version = version;

        public string Version { get; }

        public int ApplyCount { get; private set; }

        public Task ApplyAsync(IMongoDatabase database, CancellationToken cancellationToken = default)
        {
            ApplyCount++;
            return Task.CompletedTask;
        }
    }
}
