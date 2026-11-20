using CodeHollow.FeedReader;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ohrbob.Api.Services;
using Ohrbob.Server.Shared.Data;
using Feed = Ohrbob.Server.Shared.Data.Feed;

namespace Ohrbob.Api.Tests.ServiceTests;

public class FeedUpdateServiceTests
{
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    private ApiTestApp<Program> _app = null!;
    private FeedUpdateService _service = null!;
    private FeedsService _feedsService = null!;
    private EpisodesService _episodesService = null!;
    private StubFeedReader _reader = null!;

    [SetUp]
    public void Setup()
    {
        _reader = new StubFeedReader();
        _app = new ApiTestApp<Program>(
            serviceOverride: services =>
            {
                services.AddSingleton<IFeedReader>(_reader);
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
            },
            configurationOverride: configuration =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    {"MongoDbLogs:ConnectionString", "mongodb://localhost:27017/TestsOhrbobLogs"},
                    {"MongoDbLogs:DatabaseName", "TestsOhrbobLogs"},
                    {"MongoDbData:ConnectionString", "mongodb://localhost:27017/TestsOhrbobData"},
                    {"MongoDbData:DatabaseName", "TestsOhrbobData"}
                });
            },
            setupMongoDbContext: true,
            createDefaultClient: true);
        _service = _app.Services.GetRequiredService<FeedUpdateService>();
        _feedsService = _app.Services.GetRequiredService<FeedsService>();
        _episodesService = _app.Services.GetRequiredService<EpisodesService>();
    }

    [TearDown]
    public void TearDown()
    {
        _app?.Dispose();
    }

    private static DateTime Utc(int month, int day, int hour, int minute = 0) =>
        new(2026, month, day, hour, minute, 0, DateTimeKind.Utc);

    private static ParsedFeed WeeklyFeed(string title = "Weekly Show", string? xml = null)
    {
        var items = new[]
        {
            new FeedItem { Id = "ep-1", Title = "One", PublishingDate = Utc(10, 6, 6) },
            new FeedItem { Id = "ep-2", Title = "Two", PublishingDate = Utc(10, 13, 6) },
            new FeedItem { Id = "ep-3", Title = "Three", PublishingDate = Utc(10, 20, 6) },
            new FeedItem { Id = "ep-4", Title = "Four", PublishingDate = Utc(10, 27, 6) },
            new FeedItem { Id = "ep-5", Title = "Five", PublishingDate = Utc(11, 3, 6) }
        };

        return new ParsedFeed(title, "Description", "https://img/cover.png", xml, items);
    }

    [Test]
    public async Task RefreshAsync_StoresNewEpisodesAndSchedulesTheNextPublishSlot()
    {
        var feed = new Feed { Url = "https://example.com/weekly.xml", Title = "Old Title" };
        await _feedsService.CreateAsync(feed);
        _reader.Result = WeeklyFeed();

        var result = await _service.RefreshAsync(feed.Id);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Success, Is.True);
        Assert.That(result.EpisodeCount, Is.EqualTo(5));

        var stored = await _feedsService.GetAsync(feed.Id);
        Assert.That(stored!.Title, Is.EqualTo("Weekly Show"));
        Assert.That(stored.LastUpdated, Is.Not.Null);
        Assert.That(stored.NextUpdateAt, Is.EqualTo(Utc(11, 10, 6).AddMinutes(1)));
    }

    [Test]
    public async Task RefreshAsync_StoresTheUpdateFrequencyRruleAndPrefersItForScheduling()
    {
        var feed = new Feed { Url = "https://example.com/rrule.xml", Title = "Show" };
        await _feedsService.CreateAsync(feed);
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <rss version="2.0" xmlns:podcast="https://podcastindex.org/namespace/1.0">
              <channel>
                <title>Show</title>
                <podcast:updateFrequency rrule="FREQ=WEEKLY;BYDAY=MO,WE">twice weekly</podcast:updateFrequency>
              </channel>
            </rss>
            """;
        _reader.Result = WeeklyFeed(xml: xml);

        var result = await _service.RefreshAsync(feed.Id);

        Assert.That(result!.Success, Is.True);
        var stored = await _feedsService.GetAsync(feed.Id);
        Assert.That(stored!.UpdateFrequencyRrule, Is.EqualTo("FREQ=WEEKLY;BYDAY=MO,WE"));
        Assert.That(stored.NextUpdateAt, Is.EqualTo(Utc(11, 9, 6).AddMinutes(1)));
    }

    [Test]
    public async Task RefreshAsync_ReadFailure_ReturnsFailureAndSchedulesARetry()
    {
        var feed = new Feed { Url = "https://example.com/broken.xml", Title = "Broken" };
        await _feedsService.CreateAsync(feed);
        _reader.Failure = new InvalidOperationException("feed is down");

        var result = await _service.RefreshAsync(feed.Id);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Success, Is.False);

        var stored = await _feedsService.GetAsync(feed.Id);
        Assert.That(stored!.NextUpdateAt, Is.EqualTo(Now.AddMinutes(15)));
        Assert.That(await _episodesService.CountByFeedIdAsync(feed.Id), Is.EqualTo(0));
    }

    [Test]
    public async Task RefreshAsync_FeedWithoutEpisodes_RetriesWithinAMinute()
    {
        // Newly added feeds that have not published yet are polled again soon, like the
        // old worker did, instead of waiting a full hour.
        var feed = new Feed { Url = "https://example.com/empty.xml", Title = "Empty" };
        await _feedsService.CreateAsync(feed);
        _reader.Result = new ParsedFeed("Empty", "Description", null, null, Array.Empty<FeedItem>());

        var result = await _service.RefreshAsync(feed.Id);

        Assert.That(result!.Success, Is.True);
        var stored = await _feedsService.GetAsync(feed.Id);
        Assert.That(stored!.NextUpdateAt, Is.EqualTo(Now.AddMinutes(1)));
    }

    [Test]
    public async Task RefreshAsync_UnknownFeed_ReturnsNull()
    {
        Assert.That(await _service.RefreshAsync("64b7f9b2f1a4c3d5e6f7a8b9"), Is.Null);
    }

    [Test]
    public void IsDue_UsesTheStoredSchedule()
    {
        var feed = new Feed { NextUpdateAt = Now.AddMinutes(5) };
        Assert.That(FeedUpdateService.IsDue(feed, Now), Is.False);

        feed.NextUpdateAt = Now.AddMinutes(-1);
        Assert.That(FeedUpdateService.IsDue(feed, Now), Is.True);
    }

    [Test]
    public void IsDue_LegacyFeedWithoutSchedule_UsesTheHourlyFallback()
    {
        var feed = new Feed { LastUpdated = Now.AddMinutes(-30) };
        Assert.That(FeedUpdateService.IsDue(feed, Now), Is.False);

        feed.LastUpdated = Now.AddMinutes(-61);
        Assert.That(FeedUpdateService.IsDue(feed, Now), Is.True);
    }

    private sealed class StubFeedReader : IFeedReader
    {
        public ParsedFeed? Result { get; set; }
        public Exception? Failure { get; set; }

        public Task<ParsedFeed> ReadAsync(string url, CancellationToken cancellationToken = default)
        {
            if (Failure != null) return Task.FromException<ParsedFeed>(Failure);
            return Task.FromResult(Result ?? throw new InvalidOperationException("No result configured."));
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTime now) => _now = new DateTimeOffset(now);
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
