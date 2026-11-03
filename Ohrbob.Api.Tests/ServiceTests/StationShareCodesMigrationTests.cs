using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Ohrbob.Api.Configuration;
using Ohrbob.Api.Migrations;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Tests.ServiceTests;

public class StationShareCodesMigrationTests
{
    private const string DatabaseName = "TestsOhrbobShareCodes";
    private MongoDbContext _dbContext = null!;

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(ApiTestApp<Program>.MongodbConnectionStringEnvVariable)
        ?? "mongodb://localhost:27017";

    [SetUp]
    public void Setup()
    {
        _dbContext = new MongoDbContext(ConnectionString, DatabaseName);
        _dbContext.GetDatabase()!.RunCommand(new JsonCommand<BsonDocument>("{ dropDatabase: 1 }"));
    }

    [TearDown]
    public void TearDown()
    {
        _dbContext.GetDatabase()!.RunCommand(new JsonCommand<BsonDocument>("{ dropDatabase: 1 }"));
    }

    private IMongoCollection<BsonDocument> RawStations =>
        _dbContext.GetDatabase()!.GetCollection<BsonDocument>("StationCollection");

    [Test]
    public async Task Migration_BackfillsLegacyStationsWithoutTheField_AndIsIdempotent()
    {
        await RawStations.InsertManyAsync(new[]
        {
            new BsonDocument { { "_id", "missing" }, { "AccountId", "a" }, { "Name", "Missing" } },
            new BsonDocument
            {
                { "_id", "fixed" }, { "AccountId", "a" }, { "Name", "Fixed" }, { "ShareCode", "Abc123XYZ9" }
            },
            new BsonDocument
            {
                { "_id", "deleted" }, { "AccountId", "a" }, { "Name", "Deleted" },
                { "DeletedAt", DateTime.UtcNow }
            }
        });

        var migrator = new DatabaseMigrator(
            _dbContext,
            new IMigration[] { new Migration_0_0_10_StationShareCodes() },
            NullLogger<DatabaseMigrator>.Instance);

        await migrator.MigrateAsync();
        await migrator.MigrateAsync();

        var missing = await RawStations.Find(new BsonDocument("_id", "missing")).FirstOrDefaultAsync();
        var fixedStation = await RawStations.Find(new BsonDocument("_id", "fixed")).FirstOrDefaultAsync();
        var deleted = await RawStations.Find(new BsonDocument("_id", "deleted")).FirstOrDefaultAsync();

        Assert.That(missing!["ShareCode"].AsString, Has.Length.EqualTo(10));
        Assert.That(fixedStation!["ShareCode"].AsString, Is.EqualTo("Abc123XYZ9"));
        Assert.That(deleted!.Contains("ShareCode"), Is.False, "Soft-deleted stations keep no share code");
    }

    [Test]
    public async Task RepairMigration_BackfillsStationsMissedByTheOldFilter()
    {
        await RawStations.InsertOneAsync(new BsonDocument
        {
            { "_id", "legacy" }, { "AccountId", "a" }, { "Name", "Legacy" }
        });

        await new Migration_0_0_11_RepairStationShareCodes()
            .ApplyAsync(_dbContext.GetDatabase()!);

        var repaired = await RawStations.Find(new BsonDocument("_id", "legacy")).FirstOrDefaultAsync();
        Assert.That(repaired!["ShareCode"].AsString, Has.Length.EqualTo(10));
    }
}
