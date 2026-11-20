using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CodeHollow.FeedReader;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ohrbob.Api.Configuration;
using Ohrbob.Api.Services;
using Ohrbob.Server.Shared.Requests;
using Feed = Ohrbob.Server.Shared.Data.Feed;

namespace Ohrbob.Api.Tests.ApiTests;

public class FeedRefreshApiTests
{
    private const string TestApiKey = "123456";
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    private ApiTestApp<Program> _app = null!;
    private HttpClient _client = null!;
    private StubFeedReader _reader = null!;
    private FeedsService _feedsService = null!;

    [SetUp]
    public void Setup()
    {
        _reader = new StubFeedReader();
        _app = new ApiTestApp<Program>(
            serviceCollection =>
            {
                serviceCollection.AddSingleton<IFeedReader>(_reader);
                serviceCollection.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
            },
            configuration =>
            {
                var configContainer = new
                {
                    MongoDbLogs = new MongoDbSettings { ConnectionString = "mongodb://localhost:27017/TestsOhrbobLogs", DatabaseName = "TestsOhrbobLogs" },
                    MongoDbData = new MongoDbSettings { ConnectionString = "mongodb://localhost:27017/TestsOhrbobData", DatabaseName = "TestsOhrbobData" },
                    ApiKeys = new[] { new { Key = TestApiKey, Name = "test", Scopes = new[] { "all" } } }
                };
                var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(configContainer);
                configuration.AddJsonStream(new MemoryStream(jsonBytes));
            },
            setupMongoDbContext: true,
            databaseName: "TestsOhrbobData");
        _client = _app.CreateDefaultClient();
        _feedsService = _app.Services.GetRequiredService<FeedsService>();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _app?.Dispose();
    }

    private HttpRequestMessage RefreshRequest(string feedId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/feeds/{feedId}/refresh");
        request.Headers.Add("X-API-KEY", TestApiKey);
        return request;
    }

    [Test]
    public async Task Refresh_UpdatesTheFeedAndReturnsItsStats()
    {
        var feed = await _feedsService.CreateAsync(new Feed
        {
            Url = "https://example.com/weekly.xml",
            Title = "Old Title"
        });
        _reader.Result = new ParsedFeed(
            "Fresh Title",
            "Description",
            "https://img/cover.png",
            null,
            new[]
            {
                new FeedItem { Id = "ep-1", Title = "One", PublishingDate = Now.AddDays(-28) },
                new FeedItem { Id = "ep-2", Title = "Two", PublishingDate = Now.AddDays(-21) },
                new FeedItem { Id = "ep-3", Title = "Three", PublishingDate = Now.AddDays(-14) },
                new FeedItem { Id = "ep-4", Title = "Four", PublishingDate = Now.AddDays(-7) }
            });

        var response = await _client.SendAsync(RefreshRequest(feed!.Id));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var dto = await response.Content.ReadFromJsonAsync<FeedRefreshResultDto>();
        Assert.That(dto, Is.Not.Null);
        Assert.That(dto!.Id, Is.EqualTo(feed.Id));
        Assert.That(dto.Title, Is.EqualTo("Fresh Title"));
        Assert.That(dto.EpisodeCount, Is.EqualTo(4));
        Assert.That(dto.LastUpdated, Is.Not.Null);
        Assert.That(dto.NextUpdateAt, Is.Not.Null);
    }

    [Test]
    public async Task Refresh_UnknownFeed_ReturnsNotFound()
    {
        var response = await _client.SendAsync(RefreshRequest("64b7f9b2f1a4c3d5e6f7a8b9"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Refresh_WithoutApiKey_ReturnsUnauthorized()
    {
        var response = await _client.PostAsync("/api/feeds/64b7f9b2f1a4c3d5e6f7a8b9/refresh", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    private sealed class StubFeedReader : IFeedReader
    {
        public ParsedFeed? Result { get; set; }

        public Task<ParsedFeed> ReadAsync(string url, CancellationToken cancellationToken = default)
        {
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
