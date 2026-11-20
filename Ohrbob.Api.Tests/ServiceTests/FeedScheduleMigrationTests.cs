using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Ohrbob.Api.Configuration;
using Ohrbob.Api.Migrations;

namespace Ohrbob.Api.Tests.ServiceTests;

public class FeedScheduleMigrationTests
{
    private const string DatabaseName = "TestsOhrbobFeedSchedules";
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

    private IMongoCollection<BsonDocument> RawFeeds =>
        _dbContext.GetDatabase()!.GetCollection<BsonDocument>("FeedCollection");

    [Test]
    public async Task Migration_BackfillsNextUpdateAt_AndIsIdempotent()
    {
        var lastUpdated = new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);
        await RawFeeds.InsertManyAsync(new[]
        {
            new BsonDocument
            {
                { "_id", "known" }, { "Url", "https://example.com/known.xml" },
                { "LastUpdated", lastUpdated }
            },
            new BsonDocument
            {
                { "_id", "never-updated" }, { "Url", "https://example.com/never.xml" }
            }
        });

        var before = DateTime.UtcNow;
        var migrator = new DatabaseMigrator(
            _dbContext,
            new IMigration[] { new Migration_0_0_16_FeedRefreshSchedules() },
            NullLogger<DatabaseMigrator>.Instance);

        await migrator.MigrateAsync();

        var known = await RawFeeds.Find(new BsonDocument("_id", "known")).FirstOrDefaultAsync();
        var never = await RawFeeds.Find(new BsonDocument("_id", "never-updated")).FirstOrDefaultAsync();

        Assert.That(known!["NextUpdateAt"].ToUniversalTime(), Is.EqualTo(lastUpdated.AddHours(1)));
        Assert.That(
            never!["NextUpdateAt"].ToUniversalTime(),
            Is.InRange(before.AddHours(1).AddMinutes(-1), DateTime.UtcNow.AddHours(1).AddMinutes(1)));

        // A second run must not move the schedule that the first one wrote.
        var knownFirstRun = known["NextUpdateAt"].ToUniversalTime();
        await migrator.MigrateAsync();

        var knownAgain = await RawFeeds.Find(new BsonDocument("_id", "known")).FirstOrDefaultAsync();
        Assert.That(knownAgain!["NextUpdateAt"].ToUniversalTime(), Is.EqualTo(knownFirstRun));
    }
}
