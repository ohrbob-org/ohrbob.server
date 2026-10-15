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
        public string? stationId { get; set; }
        public string? stationName { get; set; }
        public bool? isCompiling { get; set; }
        public bool? isEmpty { get; set; }
        public TrackInfo? current { get; set; }
        public TrackInfo? next { get; set; }
        public TrackInfo? prev { get; set; }
    }

    public class TrackInfo
    {
        public string? episodeId { get; set; }
        public string? title { get; set; }
        public string? artist { get; set; }
        public string? streamUri { get; set; }
        public string? link { get; set; }
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
        await _app.MongoDbContext.GetCollection<Feed>("FeedCollection").InsertOneAsync(new Feed { Url = "https://feed.url", Title = "Feed Title" });

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
        // Widget feature fields: feed-derived artist + absolute episode link
        Assert.That(status.current.artist, Is.EqualTo("Feed Title"));
        Assert.That(status.current.link, Is.EqualTo("https://audio/1"));
        Assert.That(status.next.artist, Is.EqualTo("Feed Title"));
        Assert.That(status.next.link, Is.EqualTo("https://audio/2"));
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
    public async Task GetStatus_IgnoresStaleEpisodeFromDeletedStation_PicksEpisodeFromActiveStation()
    {
        // Reproduces: station deleted (LastActiveStationId cleared) but LastActiveEpisodeId
        // still points at an episode of the old station. When a new station becomes active,
        // the stale episode must not be served as "current".
        var newStationId = ObjectId.GenerateNewId().ToString();
        var staleEpId = ObjectId.GenerateNewId().ToString();
        var newEpId = ObjectId.GenerateNewId().ToString();

        var profile = new Profile
        {
            AccountId = "test-account",
            StreamToken = "token-123",
            AllowedStationIds = new[] { newStationId },
            LastActiveStationId = null,
            LastActiveEpisodeId = staleEpId
        };
        var station = CreateOldestFirstStation(newStationId);
        var staleEpisode = new Episode { Id = staleEpId, Title = "Old Deleted Station Episode", FeedUrl = "https://old.feed/url", PublishedAt = DateTime.UtcNow.AddDays(-2), AudioUrl = "https://audio/old" };
        var newEpisode = new Episode { Id = newEpId, Title = "New Station Episode", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-1), AudioUrl = "https://audio/new" };

        await _app.MongoDbContext.GetCollection<Profile>("ProfileCollection").InsertOneAsync(profile);
        await _app.MongoDbContext.GetCollection<Station>("StationCollection").InsertOneAsync(station);
        await SeedEpisodesAsync(staleEpisode, newEpisode);

        _client.DefaultRequestHeaders.Add("X-Stream-Token", "token-123");
        var response = await _client.GetAsync("/stream/status");
        response.EnsureSuccessStatusCode();

        var status = await response.Content.ReadFromJsonAsync<StatusResponse>();
        Assert.That(status, Is.Not.Null);
        Assert.That(status.stationId, Is.EqualTo(newStationId));
        Assert.That(status.current, Is.Not.Null);
        Assert.That(status.current.episodeId, Is.EqualTo(newEpId));
    }

    [Test]
    public async Task GetStatus_WithDeletedActiveStation_ReturnsEmptyState()
    {
        var deletedStationId = ObjectId.GenerateNewId().ToString();
        var epId = ObjectId.GenerateNewId().ToString();

        var profile = new Profile
        {
            AccountId = "test-account",
            StreamToken = "token-123",
            LastActiveStationId = deletedStationId,
            LastActiveEpisodeId = epId
        };
        var station = CreateOldestFirstStation(deletedStationId);
        station.DeletedAt = DateTime.UtcNow;
        var episode = new Episode { Id = epId, Title = "Old Episode", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-1), AudioUrl = "https://audio/old" };

        await _app.MongoDbContext.GetCollection<Profile>("ProfileCollection").InsertOneAsync(profile);
        await _app.MongoDbContext.GetCollection<Station>("StationCollection").InsertOneAsync(station);
        await SeedEpisodesAsync(episode);

        _client.DefaultRequestHeaders.Add("X-Stream-Token", "token-123");
        var response = await _client.GetAsync("/stream/status");
        response.EnsureSuccessStatusCode();

        var status = await response.Content.ReadFromJsonAsync<StatusResponse>();
        Assert.That(status, Is.Not.Null);
        Assert.That(status.stationId, Is.Null);
        Assert.That(status.current, Is.Null);
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

    [Test]
    public async Task Prev_AdvancesToPreviousEpisode()
    {
        var stationId = ObjectId.GenerateNewId().ToString();
        var ep1Id = ObjectId.GenerateNewId().ToString();
        var ep2Id = ObjectId.GenerateNewId().ToString();

        var profile = new Profile { AccountId = "test-account", StreamToken = "token-123", LastActiveStationId = stationId, LastActiveEpisodeId = ep2Id };
        var station = new Station { Id = stationId, Name = "Test Station", Feeds = new[] { "https://feed.url" }, Schedule = new StationSchedule { Feeds = new List<StationFeedSchedule> { new StationFeedSchedule { Url = "https://feed.url", SortOrder = FeedSortOrder.OldestToNewest } } } };
        var ep1 = new Episode { Id = ep1Id, Title = "Ep 1", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-2) };
        var ep2 = new Episode { Id = ep2Id, Title = "Ep 2", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-1) };

        var profilesCol = _app.MongoDbContext.GetCollection<Profile>("ProfileCollection");
        await profilesCol.InsertOneAsync(profile);
        await _app.MongoDbContext.GetCollection<Station>("StationCollection").InsertOneAsync(station);
        await _app.MongoDbContext.GetCollection<Episode>("EpisodeCollection").InsertManyAsync(new[] { ep1, ep2 });

        _client.DefaultRequestHeaders.Add("X-Stream-Token", "token-123");

        var response = await _client.PostAsync("/stream/prev", null);
        response.EnsureSuccessStatusCode();

        var updatedProfile = await profilesCol.Find(p => p.Id == profile.Id).FirstOrDefaultAsync();
        Assert.That(updatedProfile.LastActiveEpisodeId, Is.EqualTo(ep1Id));
    }

    [Test]
    public async Task Prev_AtFirstEpisode_IsNoOp()
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

        var response = await _client.PostAsync("/stream/prev", null);
        response.EnsureSuccessStatusCode();

        var updatedProfile = await profilesCol.Find(p => p.Id == profile.Id).FirstOrDefaultAsync();
        Assert.That(updatedProfile.LastActiveEpisodeId, Is.EqualTo(ep1Id));
    }

    [Test]
    public async Task GetStatus_IncludesPrevEpisode()
    {
        var stationId = ObjectId.GenerateNewId().ToString();
        var ep1Id = ObjectId.GenerateNewId().ToString();
        var ep2Id = ObjectId.GenerateNewId().ToString();
        var ep3Id = ObjectId.GenerateNewId().ToString();

        var profile = new Profile { AccountId = "test-account", StreamToken = "token-123", LastActiveStationId = stationId, LastActiveEpisodeId = ep2Id };
        var station = new Station { Id = stationId, Name = "Test Station", Feeds = new[] { "https://feed.url" }, Schedule = new StationSchedule { Feeds = new List<StationFeedSchedule> { new StationFeedSchedule { Url = "https://feed.url", SortOrder = FeedSortOrder.OldestToNewest } } } };
        var ep1 = new Episode { Id = ep1Id, Title = "Ep 1", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-3), AudioUrl = "https://audio/1" };
        var ep2 = new Episode { Id = ep2Id, Title = "Ep 2", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-2), AudioUrl = "https://audio/2" };
        var ep3 = new Episode { Id = ep3Id, Title = "Ep 3", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-1), AudioUrl = "https://audio/3" };

        await _app.MongoDbContext.GetCollection<Profile>("ProfileCollection").InsertOneAsync(profile);
        await _app.MongoDbContext.GetCollection<Station>("StationCollection").InsertOneAsync(station);
        await _app.MongoDbContext.GetCollection<Episode>("EpisodeCollection").InsertManyAsync(new[] { ep1, ep2, ep3 });

        _client.DefaultRequestHeaders.Add("X-Stream-Token", "token-123");
        var response = await _client.GetAsync("/stream/status");
        response.EnsureSuccessStatusCode();

        var status = await response.Content.ReadFromJsonAsync<StatusResponse>();
        Assert.That(status, Is.Not.Null);
        Assert.That(status.current, Is.Not.Null);
        Assert.That(status.current.title, Is.EqualTo("Ep 2"));
        Assert.That(status.prev, Is.Not.Null);
        Assert.That(status.prev.title, Is.EqualTo("Ep 1"));
        Assert.That(status.prev.link, Is.EqualTo("https://audio/1"));
        // "next" is positional: the next unplayed episode after the current position
        Assert.That(status.next, Is.Not.Null);
        Assert.That(status.next.title, Is.EqualTo("Ep 3"));
    }

    private static Station CreateOldestFirstStation(string stationId) =>
        new() { Id = stationId, Name = "Test Station", Feeds = new[] { "https://feed.url" }, Schedule = new StationSchedule { Feeds = new List<StationFeedSchedule> { new StationFeedSchedule { Url = "https://feed.url", SortOrder = FeedSortOrder.OldestToNewest } } } };

    private async Task SeedEpisodesAsync(params Episode[] episodes) =>
        await _app.MongoDbContext.GetCollection<Episode>("EpisodeCollection").InsertManyAsync(episodes);

    [Test]
    public async Task Next_IsPositional_ReturnsEpisodeAfterCurrent()
    {
        var stationId = ObjectId.GenerateNewId().ToString();
        var ep1Id = ObjectId.GenerateNewId().ToString();
        var ep2Id = ObjectId.GenerateNewId().ToString();
        var ep3Id = ObjectId.GenerateNewId().ToString();

        var profile = new Profile { AccountId = "test-account", StreamToken = "token-123", LastActiveStationId = stationId, LastActiveEpisodeId = ep2Id };
        var profilesCol = _app.MongoDbContext.GetCollection<Profile>("ProfileCollection");
        await profilesCol.InsertOneAsync(profile);
        await _app.MongoDbContext.GetCollection<Station>("StationCollection").InsertOneAsync(CreateOldestFirstStation(stationId));
        await SeedEpisodesAsync(
            new Episode { Id = ep1Id, Title = "Ep 1", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-3) },
            new Episode { Id = ep2Id, Title = "Ep 2", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-2) },
            new Episode { Id = ep3Id, Title = "Ep 3", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-1) });

        _client.DefaultRequestHeaders.Add("X-Stream-Token", "token-123");
        var response = await _client.PostAsync("/stream/next", null);
        response.EnsureSuccessStatusCode();

        var updatedProfile = await profilesCol.Find(p => p.Id == profile.Id).FirstOrDefaultAsync();
        Assert.That(updatedProfile.LastActiveEpisodeId, Is.EqualTo(ep3Id));
    }

    [Test]
    public async Task Next_SkipsListenedEpisode_AheadOfCurrent()
    {
        var stationId = ObjectId.GenerateNewId().ToString();
        var ep1Id = ObjectId.GenerateNewId().ToString();
        var ep2Id = ObjectId.GenerateNewId().ToString();
        var ep3Id = ObjectId.GenerateNewId().ToString();

        var profile = new Profile { AccountId = "test-account", StreamToken = "token-123", LastActiveStationId = stationId, LastActiveEpisodeId = ep1Id };
        var profilesCol = _app.MongoDbContext.GetCollection<Profile>("ProfileCollection");
        await profilesCol.InsertOneAsync(profile);
        await _app.MongoDbContext.GetCollection<Station>("StationCollection").InsertOneAsync(CreateOldestFirstStation(stationId));
        await SeedEpisodesAsync(
            new Episode { Id = ep1Id, Title = "Ep 1", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-3) },
            new Episode { Id = ep2Id, Title = "Ep 2", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-2) },
            new Episode { Id = ep3Id, Title = "Ep 3", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-1) });
        await _app.MongoDbContext.GetCollection<ListenState>("ListenStateCollection").InsertOneAsync(
            new ListenState { UserId = profile.Id, StationId = stationId, EpisodeId = ep2Id, IsListened = true });

        _client.DefaultRequestHeaders.Add("X-Stream-Token", "token-123");
        var response = await _client.PostAsync("/stream/next", null);
        response.EnsureSuccessStatusCode();

        var updatedProfile = await profilesCol.Find(p => p.Id == profile.Id).FirstOrDefaultAsync();
        Assert.That(updatedProfile.LastActiveEpisodeId, Is.EqualTo(ep3Id));
    }

    [Test]
    public async Task Prev_LandsOnListenedEpisode_Positionally()
    {
        var stationId = ObjectId.GenerateNewId().ToString();
        var ep1Id = ObjectId.GenerateNewId().ToString();
        var ep2Id = ObjectId.GenerateNewId().ToString();

        var profile = new Profile { AccountId = "test-account", StreamToken = "token-123", LastActiveStationId = stationId, LastActiveEpisodeId = ep2Id };
        var profilesCol = _app.MongoDbContext.GetCollection<Profile>("ProfileCollection");
        await profilesCol.InsertOneAsync(profile);
        await _app.MongoDbContext.GetCollection<Station>("StationCollection").InsertOneAsync(CreateOldestFirstStation(stationId));
        await SeedEpisodesAsync(
            new Episode { Id = ep1Id, Title = "Ep 1", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-2) },
            new Episode { Id = ep2Id, Title = "Ep 2", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-1) });
        await _app.MongoDbContext.GetCollection<ListenState>("ListenStateCollection").InsertOneAsync(
            new ListenState { UserId = profile.Id, StationId = stationId, EpisodeId = ep1Id, IsListened = true });

        _client.DefaultRequestHeaders.Add("X-Stream-Token", "token-123");
        var response = await _client.PostAsync("/stream/prev", null);
        response.EnsureSuccessStatusCode();

        var updatedProfile = await profilesCol.Find(p => p.Id == profile.Id).FirstOrDefaultAsync();
        Assert.That(updatedProfile.LastActiveEpisodeId, Is.EqualTo(ep1Id));
    }
}
