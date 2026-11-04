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
    public async Task GetStatus_Subscriber_OnlySeesHostCompletedEpisodes()
    {
        var stationId = ObjectId.GenerateNewId().ToString();
        var ep1 = new Episode { Id = ObjectId.GenerateNewId().ToString(), Title = "Completed by host", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-2), AudioUrl = "https://audio/1" };
        var ep2 = new Episode { Id = ObjectId.GenerateNewId().ToString(), Title = "Not completed by host", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-1), AudioUrl = "https://audio/2" };

        var profile = new Profile
        {
            AccountId = "subscriber", StreamToken = "token-123", AllowedStationIds = new[] { stationId }
        };
        var station = CreateOldestFirstStation(stationId);
        station.AccountId = "owner-account";
        station.HostPaceEnabled = true;
        station.HostProfileId = "host-profile";

        await _app.MongoDbContext.GetCollection<Profile>("ProfileCollection").InsertOneAsync(profile);
        await _app.MongoDbContext.GetCollection<Station>("StationCollection").InsertOneAsync(station);
        await SeedEpisodesAsync(ep1, ep2);
        await _app.MongoDbContext.GetCollection<ListenState>("ListenStateCollection").InsertOneAsync(new ListenState
        {
            UserId = "host-profile", EpisodeId = ep1.Id, StationId = stationId, IsListened = true
        });

        _client.DefaultRequestHeaders.Add("X-Stream-Token", "token-123");
        var response = await _client.GetAsync("/stream/status");
        response.EnsureSuccessStatusCode();

        var status = await response.Content.ReadFromJsonAsync<StatusResponse>();
        Assert.That(status!.current, Is.Not.Null);
        Assert.That(status.current!.episodeId, Is.EqualTo(ep1.Id));
        Assert.That(status.next, Is.Null, "The host has not completed a second episode");
    }

    [Test]
    public async Task Next_Subscriber_StopsAtHostFrontier()
    {
        var stationId = ObjectId.GenerateNewId().ToString();
        var ep1 = new Episode { Id = ObjectId.GenerateNewId().ToString(), Title = "Ep 1", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-2) };
        var ep2 = new Episode { Id = ObjectId.GenerateNewId().ToString(), Title = "Ep 2", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-1) };
        var ep3 = new Episode { Id = ObjectId.GenerateNewId().ToString(), Title = "Ep 3", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow };

        var profile = new Profile
        {
            AccountId = "subscriber", StreamToken = "token-123",
            LastActiveStationId = stationId, LastActiveEpisodeId = ep1.Id
        };
        var station = CreateOldestFirstStation(stationId);
        station.AccountId = "owner-account";
        station.HostPaceEnabled = true;
        station.HostProfileId = "host-profile";

        var profilesCol = _app.MongoDbContext.GetCollection<Profile>("ProfileCollection");
        await profilesCol.InsertOneAsync(profile);
        await _app.MongoDbContext.GetCollection<Station>("StationCollection").InsertOneAsync(station);
        await SeedEpisodesAsync(ep1, ep2, ep3);
        await _app.MongoDbContext.GetCollection<ListenState>("ListenStateCollection").InsertManyAsync(new[]
        {
            new ListenState { Id = Guid.NewGuid().ToString(), UserId = "host-profile", EpisodeId = ep1.Id, StationId = stationId, IsListened = true },
            new ListenState { Id = Guid.NewGuid().ToString(), UserId = "host-profile", EpisodeId = ep2.Id, StationId = stationId, IsListened = true }
        });

        _client.DefaultRequestHeaders.Add("X-Stream-Token", "token-123");
        var first = await _client.PostAsync("/stream/next", null);
        first.EnsureSuccessStatusCode();

        var updated = await profilesCol.Find(p => p.Id == profile.Id).FirstOrDefaultAsync();
        Assert.That(updated!.LastActiveEpisodeId, Is.EqualTo(ep2.Id));

        var second = await _client.PostAsync("/stream/next", null);
        second.EnsureSuccessStatusCode();

        var afterFrontier = await profilesCol.Find(p => p.Id == profile.Id).FirstOrDefaultAsync();
        Assert.That(afterFrontier!.LastActiveEpisodeId, Is.EqualTo(ep2.Id), "There is no host-completed episode after ep2");
    }

    [Test]
    public async Task GetStations_IncludesSubscribedStations()
    {
        var profile = new Profile
        {
            AccountId = "subscriber", StreamToken = "token-123",
            AllowedStationIds = new[] { "owned-station" },
            SubscribedStationIds = new[] { "subscribed-station" }
        };
        await _app.MongoDbContext.GetCollection<Profile>("ProfileCollection").InsertOneAsync(profile);
        await _app.MongoDbContext.GetCollection<Station>("StationCollection").InsertManyAsync(new[]
        {
            new Station { Id = "owned-station", Name = "Owned", Feeds = Array.Empty<string>() },
            new Station { Id = "subscribed-station", Name = "Subscribed", Feeds = Array.Empty<string>() }
        });

        _client.DefaultRequestHeaders.Add("X-Stream-Token", "token-123");
        var response = await _client.GetAsync("/stream/stations");
        response.EnsureSuccessStatusCode();

        var stations = await response.Content.ReadFromJsonAsync<List<Ohrbob.Server.Shared.Requests.StationWithFeedsDto>>();
        Assert.That(stations!.Select(s => s.Station.Id), Is.EquivalentTo(new[] { "owned-station", "subscribed-station" }));
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

    private static Station CreateOldestFirstStation(string stationId, string feedUrl = "https://feed.url") =>
        new() { Id = stationId, Name = "Test Station", Feeds = new[] { feedUrl }, Schedule = new StationSchedule { Feeds = new List<StationFeedSchedule> { new StationFeedSchedule { Url = feedUrl, SortOrder = FeedSortOrder.OldestToNewest } } } };

    private static Station CreateNewestFirstStation(string stationId, string feedUrl = "https://feed.url") =>
        new() { Id = stationId, Name = "Test Station", Feeds = new[] { feedUrl }, Schedule = new StationSchedule { Feeds = new List<StationFeedSchedule> { new StationFeedSchedule { Url = feedUrl, SortOrder = FeedSortOrder.NewestToOldest } } } };

    [Test]
    public async Task GetStatus_AfterFeedUpdateAddsNewEpisode_KeepsCurrentEpisodeAndPlayhead()
    {
        // Refs #9: the feed was updated while an episode was being listened to, adding a
        // newer episode. The current episode must continue (at its playhead); the new
        // episode may only become current once the old one has finished.
        var stationId = ObjectId.GenerateNewId().ToString();
        var listenedId = ObjectId.GenerateNewId().ToString();
        var currentId = ObjectId.GenerateNewId().ToString();
        var newId = ObjectId.GenerateNewId().ToString();

        var profile = new Profile
        {
            AccountId = "test-account",
            StreamToken = "token-123",
            AllowedStationIds = new[] { stationId },
            LastActiveStationId = stationId,
            LastActiveEpisodeId = currentId
        };

        await _app.MongoDbContext.GetCollection<Profile>("ProfileCollection").InsertOneAsync(profile);
        await _app.MongoDbContext.GetCollection<Station>("StationCollection").InsertOneAsync(CreateNewestFirstStation(stationId));
        await SeedEpisodesAsync(
            new Episode { Id = listenedId, Title = "Older", FeedUrl = "https://feed.url", PublishedAt = new DateTime(2026, 10, 6, 20, 1, 0), AudioUrl = "https://audio/older" },
            new Episode { Id = currentId, Title = "Playing", FeedUrl = "https://feed.url", PublishedAt = new DateTime(2026, 10, 20, 20, 1, 0), AudioUrl = "https://audio/playing" });
        var listenStateCol = _app.MongoDbContext.GetCollection<ListenState>("ListenStateCollection");
        await listenStateCol.InsertOneAsync(new ListenState { Id = ObjectId.GenerateNewId().ToString(), UserId = profile.Id, StationId = stationId, EpisodeId = listenedId, IsListened = true });
        await listenStateCol.InsertOneAsync(new ListenState { Id = ObjectId.GenerateNewId().ToString(), UserId = profile.Id, StationId = stationId, EpisodeId = currentId, PositionSeconds = 1816, IsListened = false });

        _client.DefaultRequestHeaders.Add("X-Stream-Token", "token-123");

        var before = await (await _client.GetAsync("/stream/status")).Content.ReadFromJsonAsync<StatusResponse>();
        Assert.That(before!.current!.episodeId, Is.EqualTo(currentId));

        // Feed worker appends the new episode (append-only sync).
        await SeedEpisodesAsync(
            new Episode { Id = newId, Title = "New", FeedUrl = "https://feed.url", PublishedAt = new DateTime(2026, 10, 27, 22, 1, 0), AudioUrl = "https://audio/new" });

        var response = await _client.GetAsync("/stream/status");
        response.EnsureSuccessStatusCode();
        var after = await response.Content.ReadFromJsonAsync<StatusResponse>();

        Assert.That(after!.current!.episodeId, Is.EqualTo(currentId), "The new episode must not take over playback.");
        Assert.That(after.current.playheadPositionSeconds, Is.EqualTo(1816));
        Assert.That(after.next!.episodeId, Is.EqualTo(newId), "The new episode is next once the current one is finished.");

        // Finishing the current episode is the only thing that moves playback on.
        (await _client.PostAsync("/stream/next", null)).EnsureSuccessStatusCode();

        var advanced = await (await _client.GetAsync("/stream/status")).Content.ReadFromJsonAsync<StatusResponse>();
        Assert.That(advanced!.current!.episodeId, Is.EqualTo(newId));
    }

    [Test]
    public async Task PlayStation_OnAlreadyActiveStation_KeepsCurrentEpisode()
    {
        // Refs #9: clients select the active station on startup (the app re-syncs its
        // selection after fetching /stream/status). That must not discard the episode in
        // progress, otherwise the fallback picks the first unplayed episode - after a feed
        // update that is the newly added, newer episode instead of the unfinished one.
        var stationId = ObjectId.GenerateNewId().ToString();
        var currentId = ObjectId.GenerateNewId().ToString();
        var newId = ObjectId.GenerateNewId().ToString();

        var profile = new Profile
        {
            AccountId = "test-account",
            StreamToken = "token-123",
            AllowedStationIds = new[] { stationId },
            LastActiveStationId = stationId,
            LastActiveEpisodeId = currentId
        };

        var profilesCol = _app.MongoDbContext.GetCollection<Profile>("ProfileCollection");
        await profilesCol.InsertOneAsync(profile);
        await _app.MongoDbContext.GetCollection<Station>("StationCollection").InsertOneAsync(CreateNewestFirstStation(stationId));
        await SeedEpisodesAsync(
            new Episode { Id = currentId, Title = "Playing", FeedUrl = "https://feed.url", PublishedAt = new DateTime(2026, 10, 20, 20, 1, 0), AudioUrl = "https://audio/playing" });
        await _app.MongoDbContext.GetCollection<ListenState>("ListenStateCollection").InsertOneAsync(
            new ListenState { Id = ObjectId.GenerateNewId().ToString(), UserId = profile.Id, StationId = stationId, EpisodeId = currentId, PositionSeconds = 1816, IsListened = false });

        _client.DefaultRequestHeaders.Add("X-Stream-Token", "token-123");

        // Feed worker appends the newer episode.
        await SeedEpisodesAsync(
            new Episode { Id = newId, Title = "New", FeedUrl = "https://feed.url", PublishedAt = new DateTime(2026, 10, 27, 22, 1, 0), AudioUrl = "https://audio/new" });

        (await _client.PostAsync($"/stream/play-station/{stationId}", null)).EnsureSuccessStatusCode();

        var status = await (await _client.GetAsync("/stream/status")).Content.ReadFromJsonAsync<StatusResponse>();
        Assert.That(status!.current!.episodeId, Is.EqualTo(currentId),
            "Re-selecting the active station must keep the episode in progress.");
        Assert.That(status.current.playheadPositionSeconds, Is.EqualTo(1816));
    }

    [Test]
    public async Task PlayStation_OnOtherStation_StartsFromStrategy()
    {
        var stationId = ObjectId.GenerateNewId().ToString();
        var otherStationId = ObjectId.GenerateNewId().ToString();
        var oldEpisodeId = ObjectId.GenerateNewId().ToString();
        var otherEpisodeId = ObjectId.GenerateNewId().ToString();

        var profile = new Profile
        {
            AccountId = "test-account",
            StreamToken = "token-123",
            AllowedStationIds = new[] { stationId, otherStationId },
            LastActiveStationId = stationId,
            LastActiveEpisodeId = oldEpisodeId
        };

        await _app.MongoDbContext.GetCollection<Profile>("ProfileCollection").InsertOneAsync(profile);
        var stations = _app.MongoDbContext.GetCollection<Station>("StationCollection");
        await stations.InsertOneAsync(CreateOldestFirstStation(stationId, "https://feed.a"));
        await stations.InsertOneAsync(CreateOldestFirstStation(otherStationId, "https://feed.b"));
        await SeedEpisodesAsync(
            new Episode { Id = oldEpisodeId, Title = "Old", FeedUrl = "https://feed.a", PublishedAt = DateTime.UtcNow.AddDays(-2) },
            new Episode { Id = otherEpisodeId, Title = "Other", FeedUrl = "https://feed.b", PublishedAt = DateTime.UtcNow.AddDays(-1) });

        _client.DefaultRequestHeaders.Add("X-Stream-Token", "token-123");

        (await _client.PostAsync($"/stream/play-station/{otherStationId}", null)).EnsureSuccessStatusCode();

        var status = await (await _client.GetAsync("/stream/status")).Content.ReadFromJsonAsync<StatusResponse>();
        Assert.That(status!.stationId, Is.EqualTo(otherStationId));
        Assert.That(status.current!.episodeId, Is.EqualTo(otherEpisodeId));
    }

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
    public async Task Next_EndlessSpin_WrapsAroundAndContinuesInOrder()
    {
        var stationId = ObjectId.GenerateNewId().ToString();
        var ep1Id = ObjectId.GenerateNewId().ToString();
        var ep2Id = ObjectId.GenerateNewId().ToString();
        var ep3Id = ObjectId.GenerateNewId().ToString();

        var profile = new Profile { AccountId = "test-account", StreamToken = "token-123", LastActiveStationId = stationId, LastActiveEpisodeId = ep3Id };
        var station = CreateOldestFirstStation(stationId);
        station.EndlessSpin = true;

        var profilesCol = _app.MongoDbContext.GetCollection<Profile>("ProfileCollection");
        await profilesCol.InsertOneAsync(profile);
        await _app.MongoDbContext.GetCollection<Station>("StationCollection").InsertOneAsync(station);
        await SeedEpisodesAsync(
            new Episode { Id = ep1Id, Title = "Ep 1", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-3) },
            new Episode { Id = ep2Id, Title = "Ep 2", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-2) },
            new Episode { Id = ep3Id, Title = "Ep 3", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-1) });
        await _app.MongoDbContext.GetCollection<ListenState>("ListenStateCollection").InsertManyAsync(new[]
        {
            new ListenState { Id = ObjectId.GenerateNewId().ToString(), UserId = profile.Id, StationId = stationId, EpisodeId = ep1Id, IsListened = true },
            new ListenState { Id = ObjectId.GenerateNewId().ToString(), UserId = profile.Id, StationId = stationId, EpisodeId = ep2Id, IsListened = true },
            new ListenState { Id = ObjectId.GenerateNewId().ToString(), UserId = profile.Id, StationId = stationId, EpisodeId = ep3Id, IsListened = true }
        });

        _client.DefaultRequestHeaders.Add("X-Stream-Token", "token-123");

        var response = await _client.PostAsync("/stream/next", null);
        response.EnsureSuccessStatusCode();
        var updatedProfile = await profilesCol.Find(p => p.Id == profile.Id).FirstOrDefaultAsync();
        Assert.That(updatedProfile.LastActiveEpisodeId, Is.EqualTo(ep1Id), "The last episode must wrap around to the first one");

        var secondResponse = await _client.PostAsync("/stream/next", null);
        secondResponse.EnsureSuccessStatusCode();
        updatedProfile = await profilesCol.Find(p => p.Id == profile.Id).FirstOrDefaultAsync();
        Assert.That(updatedProfile.LastActiveEpisodeId, Is.EqualTo(ep2Id), "Playback must keep going forward after wrapping");
    }

    [Test]
    public async Task Prev_EndlessSpin_WrapsToLastEpisode()
    {
        var stationId = ObjectId.GenerateNewId().ToString();
        var ep1Id = ObjectId.GenerateNewId().ToString();
        var ep2Id = ObjectId.GenerateNewId().ToString();

        var profile = new Profile { AccountId = "test-account", StreamToken = "token-123", LastActiveStationId = stationId, LastActiveEpisodeId = ep1Id };
        var station = CreateOldestFirstStation(stationId);
        station.EndlessSpin = true;

        var profilesCol = _app.MongoDbContext.GetCollection<Profile>("ProfileCollection");
        await profilesCol.InsertOneAsync(profile);
        await _app.MongoDbContext.GetCollection<Station>("StationCollection").InsertOneAsync(station);
        await SeedEpisodesAsync(
            new Episode { Id = ep1Id, Title = "Ep 1", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-2) },
            new Episode { Id = ep2Id, Title = "Ep 2", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-1) });

        _client.DefaultRequestHeaders.Add("X-Stream-Token", "token-123");

        var response = await _client.PostAsync("/stream/prev", null);
        response.EnsureSuccessStatusCode();

        var updatedProfile = await profilesCol.Find(p => p.Id == profile.Id).FirstOrDefaultAsync();
        Assert.That(updatedProfile.LastActiveEpisodeId, Is.EqualTo(ep2Id));
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
