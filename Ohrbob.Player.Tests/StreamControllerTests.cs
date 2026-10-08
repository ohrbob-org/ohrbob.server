using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Text.Encodings.Web;
using System.Security.Claims;
using Ohrbob.Server.Shared.Data;
using MongoDB.Bson;
using MongoDB.Driver;
using Ohrbob.Player.Configuration;
using Ohrbob.Player.Services;
using NUnit.Framework;

namespace Ohrbob.Player.Tests;

public class StreamControllerTests
{
    private ApiTestApp<Program> _app;
    private HttpClient _client;
    private readonly string _databaseName = "TestDb_" + Guid.NewGuid().ToString();

    [SetUp]
    public void Setup()
    {
        _app = new ApiTestApp<Program>(
            databaseName: _databaseName,
            setupMongoDbContext: true,
            configurationOverride: builder => { },
            serviceOverride: services =>
            {
                services.AddAuthentication(defaultScheme: "TestScheme")
                    .AddScheme<AuthenticationSchemeOptions, PlaybackControllerTests.TestAuthHandler>("TestScheme", options => { });
            }
        );
        _client = _app.CreateClient();
        _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("TestScheme");
    }

    [TearDown]
    public void TearDown()
    {
        _app?.Dispose();
        _client?.Dispose();
    }

    public class StatusResponse
    {
        public string? stationName { get; set; }
        public TrackInfo? current { get; set; }
        public TrackInfo? next { get; set; }
    }

    public class TrackInfo
    {
        public string? episodeId { get; set; }
        public string? title { get; set; }
        public string? streamUri { get; set; }
        public int playheadPositionSeconds { get; set; }
    }

    [Test]
    public async Task GetStatus_WithoutHistory_FallsBackToFirstStation()
    {
        var stationId = ObjectId.GenerateNewId().ToString();
        var profile = new Profile { AccountId = "test-account", StreamToken = "token-123", AllowedStationIds = new[] { stationId } };
        var station = new Station { Id = stationId, Name = "Test Station", Feeds = new[] { "https://feed.url" }, Schedule = new StationSchedule { Feeds = new List<StationFeedSchedule> { new StationFeedSchedule { Url = "https://feed.url", SortOrder = FeedSortOrder.OldestToNewest } } } };
        var episode = new Episode { Id = ObjectId.GenerateNewId().ToString(), Title = "Ep 1", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-1), AudioUrl = "https://audio/1" };
        var nextEpisode = new Episode { Id = ObjectId.GenerateNewId().ToString(), Title = "Ep 2", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow, AudioUrl = "https://audio/2" };

        await _app.MongoDbContext.GetCollection<Profile>("ProfileCollection").InsertOneAsync(profile);
        await _app.MongoDbContext.GetCollection<Station>("StationCollection").InsertOneAsync(station);
        await _app.MongoDbContext.GetCollection<Episode>("EpisodeCollection").InsertManyAsync(new[] { episode, nextEpisode });

        _client.DefaultRequestHeaders.Add("X-Stream-Token", "token-123");
        var response = await _client.GetAsync("/stream/status");
        response.EnsureSuccessStatusCode();

        var status = await response.Content.ReadFromJsonAsync<StatusResponse>();
        Assert.That(status, Is.Not.Null);
        Assert.That(status.stationName, Is.EqualTo("Test Station"));
        Assert.That(status.current, Is.Not.Null);
        Assert.That(status.current.title, Is.EqualTo("Ep 1")); // Oldest episode first chronologically
        Assert.That(status.next, Is.Not.Null);
        Assert.That(status.next.title, Is.EqualTo("Ep 2"));
    }

    [Test]
    public async Task SyncStatus_UpdatesProfileAndListenState()
    {
        var stationId = ObjectId.GenerateNewId().ToString();
        var episodeId = ObjectId.GenerateNewId().ToString();
        var profile = new Profile { AccountId = "test-account", StreamToken = "token-123", AllowedStationIds = new[] { stationId } };

        var profilesCol = _app.MongoDbContext.GetCollection<Profile>("ProfileCollection");
        await profilesCol.InsertOneAsync(profile);

        _client.DefaultRequestHeaders.Add("X-Stream-Token", "token-123");
        
        var postReq = new Controllers.SyncStatusRequest { StationId = stationId, EpisodeId = episodeId, PositionSeconds = 45 };
        var response = await _client.PostAsJsonAsync("/stream/status", postReq);
        response.EnsureSuccessStatusCode();

        var updatedProfile = await profilesCol.Find(p => p.Id == profile.Id).FirstOrDefaultAsync();
        Assert.That(updatedProfile.LastActiveStationId, Is.EqualTo(stationId));
        Assert.That(updatedProfile.LastActiveEpisodeId, Is.EqualTo(episodeId));

        var listenStateCol = _app.MongoDbContext.GetCollection<ListenState>("ListenStateCollection");
        var state = await listenStateCol.Find(s => s.UserId == profile.Id && s.EpisodeId == episodeId).FirstOrDefaultAsync();
        Assert.That(state, Is.Not.Null);
        Assert.That(state.PositionSeconds, Is.EqualTo(45));
    }

    [Test]
    public async Task Next_AdvancesToNextEpisode()
    {
        var stationId = ObjectId.GenerateNewId().ToString();
        var ep1Id = ObjectId.GenerateNewId().ToString();
        var ep2Id = ObjectId.GenerateNewId().ToString();

        var profile = new Profile { AccountId = "test-account", StreamToken = "token-123", LastActiveStationId = stationId, LastActiveEpisodeId = ep1Id };
        var station = new Station { Id = stationId, Name = "Test Station", Feeds = new[] { "https://feed.url" }, Schedule = new StationSchedule { Feeds = new List<StationFeedSchedule> { new StationFeedSchedule { Url = "https://feed.url", SortOrder = FeedSortOrder.OldestToNewest } } } };
        var ep1 = new Episode { Id = ep1Id, Title = "Ep 1", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-2) };
        var ep2 = new Episode { Id = ep2Id, Title = "Ep 2", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-1) };

        var profilesCol = _app.MongoDbContext.GetCollection<Profile>("ProfileCollection");
        await profilesCol.InsertOneAsync(profile);
        await _app.MongoDbContext.GetCollection<Station>("StationCollection").InsertOneAsync(station);
        await _app.MongoDbContext.GetCollection<Episode>("EpisodeCollection").InsertManyAsync(new[] { ep1, ep2 });

        _client.DefaultRequestHeaders.Add("X-Stream-Token", "token-123");

        var response = await _client.PostAsync("/stream/next", null);
        response.EnsureSuccessStatusCode();

        var updatedProfile = await profilesCol.Find(p => p.Id == profile.Id).FirstOrDefaultAsync();
        Assert.That(updatedProfile.LastActiveEpisodeId, Is.EqualTo(ep2Id));

        // It should mark the previous episode as listened and position 0
        var listenStateCol = _app.MongoDbContext.GetCollection<ListenState>("ListenStateCollection");
        var prevEpState = await listenStateCol.Find(s => s.UserId == profile.Id && s.EpisodeId == ep1Id).FirstOrDefaultAsync();
        Assert.That(prevEpState, Is.Not.Null);
        Assert.That(prevEpState.IsListened, Is.True);
        
        var nextEpState = await listenStateCol.Find(s => s.UserId == profile.Id && s.EpisodeId == ep2Id).FirstOrDefaultAsync();
        Assert.That(nextEpState, Is.Not.Null);
        Assert.That(nextEpState.PositionSeconds, Is.EqualTo(0));
    }
}
