using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Ohrbob.Api.Configuration;
using Ohrbob.Api.Migrations;

namespace Ohrbob.Api.Tests.ServiceTests;

public class HostPaceMigrationTests
{
    private const string DatabaseName = "TestsOhrbobHostPace";
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

    [Test]
    public async Task Migration_BackfillsHostPaceDefaults_AndIsIdempotent()
    {
        var stations = _dbContext.GetDatabase()!.GetCollection<BsonDocument>("StationCollection");
        await stations.InsertOneAsync(new BsonDocument
        {
            { "_id", "legacy" }, { "AccountId", "a" }, { "Name", "Legacy" }
        });

        var migrator = new DatabaseMigrator(
            _dbContext,
            new IMigration[] { new Migration_0_0_12_HostPaceDefaults() },
            NullLogger<DatabaseMigrator>.Instance);

        await migrator.MigrateAsync();
        await migrator.MigrateAsync();

        var legacy = await stations.Find(new BsonDocument("_id", "legacy")).FirstOrDefaultAsync();
        Assert.That(legacy!.Contains("HostPaceEnabled"), Is.True);
        Assert.That(legacy["HostPaceEnabled"].AsBoolean, Is.False);
        Assert.That(legacy.Contains("HostProfileId"), Is.True);
        Assert.That(legacy["HostProfileId"].IsBsonNull, Is.True);
    }
}
