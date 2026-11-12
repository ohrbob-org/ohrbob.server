using CodeHollow.FeedReader;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Ohrbob.Api.Configuration;
using Ohrbob.Api.Migrations;
using Ohrbob.Server.Shared.Text;

namespace Ohrbob.Api.Tests.ServiceTests;

public class FeedTitleMigrationTests
{
    private const string DatabaseName = "TestsOhrbobFeedTitles";
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
    public async Task Migration_StripsHtmlFromStoredFeedTitles_AndIsIdempotent()
    {
        await RawFeeds.InsertManyAsync(new[]
        {
            new BsonDocument
            {
                { "_id", "html" }, { "Url", "https://example.com/html.xml" },
                { "Title", "<a href=\"https://example.com\">My Show</a>" }
            },
            new BsonDocument
            {
                { "_id", "plain" }, { "Url", "https://example.com/plain.xml" }, { "Title", "Plain Show" }
            }
        });

        var migrator = new DatabaseMigrator(
            _dbContext,
            new IMigration[] { new Migration_0_0_15_PlainFeedTitles() },
            NullLogger<DatabaseMigrator>.Instance);

        await migrator.MigrateAsync();
        await migrator.MigrateAsync();

        var html = await RawFeeds.Find(new BsonDocument("_id", "html")).FirstOrDefaultAsync();
        var plain = await RawFeeds.Find(new BsonDocument("_id", "plain")).FirstOrDefaultAsync();

        Assert.That(html!["Title"].AsString, Is.EqualTo("My Show"));
        Assert.That(plain!["Title"].AsString, Is.EqualTo("Plain Show"));
    }

    [TestCase("<![CDATA[<a href=\"https://example.com\">My Show</a>]]>")]
    [TestCase("&lt;a href=\"https://example.com\"&gt;My Show&lt;/a&gt;")]
    public void ParsedFeedTitle_IsSanitizedLikeIngestionDoes(string titleXml)
    {
        var xml = $"<?xml version=\"1.0\"?><rss version=\"2.0\"><channel><title>{titleXml}</title><link>https://example.com</link><description>x</description></channel></rss>";

        var parsed = FeedReader.ReadFromString(xml);

        Assert.That(HtmlText.ToPlainText(parsed.Title!), Is.EqualTo("My Show"));
    }
}
