using CodeHollow.FeedReader;
using OhrbobFeed = Ohrbob.Server.Shared.Data.Feed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ohrbob.Api.Services;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Tests.ServiceTests;

public class FeedEpisodeSyncServiceTests
{
    private ApiTestApp<Program> _app = null!;
    private EpisodesService _episodesService = null!;
    private FeedsService _feedsService = null!;

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
        _episodesService = _app.Services.GetRequiredService<EpisodesService>();
        _feedsService = _app.Services.GetRequiredService<FeedsService>();
    }

    [TearDown]
    public void TearDown()
    {
        _app?.Dispose();
    }

    [Test]
    public async Task Sync_RetainsHistoric_AddsNew_RefreshesChanged()
    {
        var feed = new OhrbobFeed { Url = "https://example.com/feed.xml", Title = "Feed", ImageUrl = "https://example.com/feed.png" };
        await _feedsService.CreateAsync(feed);

        // Historic episode no longer present in the feed must be retained.
        await _episodesService.CreateAsync(new Episode
        {
            Guid = "historic-1", FeedId = feed.Id, FeedUrl = feed.Url,
            Title = "Historic", AudioUrl = "https://example.com/historic.mp3"
        });
        // Re-seen episode with stale metadata must be refreshed.
        await _episodesService.CreateAsync(new Episode
        {
            Guid = "ep-1", FeedId = feed.Id, FeedUrl = feed.Url,
            Title = "Stale Title", AudioUrl = "https://example.com/old.mp3"
        });

        var items = new[]
        {
            new FeedItem { Id = "ep-1", Link = "https://example.com/ep1", Title = "Fresh Title", Description = "Desc", PublishingDate = DateTime.UtcNow },
            new FeedItem { Id = "ep-2", Link = "https://example.com/ep2.mp3", Title = "Brand New", Description = "Desc", PublishingDate = DateTime.UtcNow },
        };

        await FeedEpisodeSyncService.SyncAsync(_episodesService, feed, items);

        var historic = await _episodesService.FindByGuidAndFeedIdAsync("historic-1", feed.Id);
        Assert.That(historic, Is.Not.Null, "Episodes that vanished from the feed must stay in the playlist");

        var refreshed = await _episodesService.FindByGuidAndFeedIdAsync("ep-1", feed.Id);
        Assert.That(refreshed, Is.Not.Null);
        Assert.That(refreshed!.Title, Is.EqualTo("Fresh Title"));

        var added = await _episodesService.FindByGuidAndFeedIdAsync("ep-2", feed.Id);
        Assert.That(added, Is.Not.Null);
        Assert.That(added!.Title, Is.EqualTo("Brand New"));
        Assert.That(added.AudioUrl, Is.EqualTo("https://example.com/ep2.mp3"));
    }

    [Test]
    public async Task Sync_PopulatesPlainDescription_FromEntityEncodedHtml()
    {
        var feed = new OhrbobFeed { Url = "https://example.com/entity-feed.xml", Title = "Entity Feed" };
        await _feedsService.CreateAsync(feed);

        const string rawHtml = "<p>R&#252;cktritt von B&#228;rbel &amp; gr&#246;&#223;te</p>";
        var items = new[]
        {
            new FeedItem
            {
                Id = "ep-entity",
                Link = "https://example.com/entity.mp3",
                Title = "Entity",
                Description = rawHtml,
                PublishingDate = DateTime.UtcNow
            },
        };

        await FeedEpisodeSyncService.SyncAsync(_episodesService, feed, items);

        var episode = await _episodesService.FindByGuidAndFeedIdAsync("ep-entity", feed.Id);
        Assert.That(episode, Is.Not.Null);
        Assert.That(episode!.PlainDescription, Is.EqualTo("Rücktritt von Bärbel & größte"),
            "The plain-text field must have the entities decoded and the tags stripped");
        Assert.That(episode.Description, Is.EqualTo(rawHtml),
            "The raw feed HTML must be preserved unchanged");
    }
}
