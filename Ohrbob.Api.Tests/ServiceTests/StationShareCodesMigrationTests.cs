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

    [Test]
    public async Task Migration_BackfillsMissingCodes_AndIsIdempotent()
    {
        var stations = _dbContext.GetCollection<Station>("StationCollection");
        await stations.InsertManyAsync(new[]
        {
            new Station { Id = "missing", AccountId = "a", Name = "Missing", ShareCode = string.Empty },
            new Station { Id = "fixed", AccountId = "a", Name = "Fixed", ShareCode = "Abc123XYZ9" }
        });

        var migrator = new DatabaseMigrator(
            _dbContext,
            new IMigration[] { new Migration_0_0_10_StationShareCodes() },
            NullLogger<DatabaseMigrator>.Instance);

        await migrator.MigrateAsync();
        await migrator.MigrateAsync();

        var missing = await stations.Find(s => s.Id == "missing").FirstOrDefaultAsync();
        var fixedStation = await stations.Find(s => s.Id == "fixed").FirstOrDefaultAsync();

        Assert.That(missing!.ShareCode, Has.Length.EqualTo(10));
        Assert.That(missing.ShareCode, Does.Match("^[A-Za-z0-9]{10}$"));
        Assert.That(fixedStation!.ShareCode, Is.EqualTo("Abc123XYZ9"));
    }
}
