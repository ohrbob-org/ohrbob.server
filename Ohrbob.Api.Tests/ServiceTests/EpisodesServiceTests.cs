using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ohrbob.Api.Services;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Tests.ServiceTests;

public class EpisodesServiceTests
{
    private ApiTestApp<Program> _app = null!;
    private EpisodesService _service = null!;

    [SetUp]
    public void Setup()
    {
        _app = new ApiTestApp<Program>(configurationOverride: configuration =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                {"MongoDbLogs:ConnectionString", "mongodb://localhost:27017/TestsOhrbobLogs"},
                {"MongoDbLogs:DatabaseName", "TestsOhrbobLogs"},
                {"MongoDbData:ConnectionString", "mongodb://localhost:27017/TestsOhrbobData"},
                {"MongoDbData:DatabaseName", "TestsOhrbobData"}
            });
        }, setupMongoDbContext: true, createDefaultClient: true);
        _service = _app.Services.GetRequiredService<EpisodesService>();
    }

    [TearDown]
    public void TearDown()
    {
        _app?.Dispose();
    }

    [Test]
    public async Task CreateAsync_And_FindByGuidAndFeedIdAsync_ShouldWork()
    {
        var guid = Guid.NewGuid().ToString();
        var feedId = Guid.NewGuid().ToString();

        var episode = new Episode
        {
            Guid = guid,
            FeedId = feedId,
            Title = "Test Episode",
            AudioUrl = "https://example.com/audio.mp3",
            FeedUrl = "https://example.com/feed.xml"
        };

        var created = await _service.CreateAsync(episode);
        Assert.That(created, Is.Not.Null);
        Assert.That(created!.Id, Is.Not.Empty);

        var found = await _service.FindByGuidAndFeedIdAsync(guid, feedId);
        Assert.That(found, Is.Not.Null);
        Assert.That(found!.Title, Is.EqualTo("Test Episode"));

        var notFound = await _service.FindByGuidAndFeedIdAsync("wrong-guid", feedId);
        Assert.That(notFound, Is.Null);
    }
}
