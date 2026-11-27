using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Ohrbob.Api.Configuration;
using Ohrbob.Api.Migrations;

namespace Ohrbob.Api.Tests.ServiceTests;

public class FeedColourMigrationTests
{
    private const string DatabaseName = "TestsOhrbobFeedColours";
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

    private IMongoCollection<BsonDocument> Stations =>
        _dbContext.GetDatabase()!.GetCollection<BsonDocument>("StationCollection");

    [Test]
    public async Task Migration_ReplacesLegacyRedWithOrange_AndIsIdempotent()
    {
        await Stations.InsertOneAsync(new BsonDocument
        {
            { "_id", "red-station" },
            { "Name", "Red" },
            { "Schedule", new BsonDocument("Feeds", new BsonArray
                {
                    new BsonDocument { { "Url", "https://one" }, { "Color", "#E63946" } },
                    new BsonDocument { { "Url", "https://two" }, { "Color", "#1D3557" } }
                })
            }
        });

        var migrator = new DatabaseMigrator(
            _dbContext,
            new IMigration[] { new Migration_0_0_17_OrangeFeedColours() },
            NullLogger<DatabaseMigrator>.Instance);

        await migrator.MigrateAsync();
        await migrator.MigrateAsync();

        var station = await Stations.Find(new BsonDocument("_id", "red-station")).FirstAsync();
        var feeds = station["Schedule"]["Feeds"].AsBsonArray;
        Assert.That(feeds[0]["Color"].AsString, Is.EqualTo("#FC5D00"));
        Assert.That(feeds[1]["Color"].AsString, Is.EqualTo("#1D3557"));
    }
}
