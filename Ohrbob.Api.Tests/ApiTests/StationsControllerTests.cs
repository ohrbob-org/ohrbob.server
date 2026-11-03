using Ohrbob.Server.Shared.Requests;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Ohrbob.Api.Configuration;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Tests.ApiTests;

public class StationsControllerTests
{
    private ApiTestApp<Program> _app;
    private HttpClient _client;
    private const string _testApiKey = "123456";

    [SetUp]
    public void Setup()
    {
        _app = new ApiTestApp<Program>(serviceCollection =>
        {
            
        }, configuration =>
        {
            var configContainer = new
            {
                MongoDbLogs = new MongoDbSettings(){ConnectionString = "mongodb://localhost:27017/TestsOhrbobLogs", DatabaseName = "TestsOhrbobLogs"},
                MongoDbData = new MongoDbSettings(){ConnectionString = "mongodb://localhost:27017/TestsOhrbobData", DatabaseName = "TestsOhrbobData"},
                ApiKeys = new []{new {
                    Key = _testApiKey,
                    Name = "test",
                    Scopes = new []{"all"}}}  
            
            };
            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(configContainer);
            configuration.AddJsonStream(new MemoryStream(jsonBytes));
            
        }, setupMongoDbContext: true, databaseName: "TestsOhrbobData");
        _client = _app.CreateDefaultClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _app?.Dispose();
    }

    [Test]
    public async Task CreateStation_Works()
    {
        var accountId = Guid.NewGuid().ToString();
        var station = new Station
        {
            Name = "My Station",
            Feeds = Array.Empty<string>()
        };

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/stations/{accountId}");
        request.Headers.Add("X-API-KEY", _testApiKey);
        request.Content = JsonContent.Create(station);

        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        Assert.That(response.IsSuccessStatusCode, $"Failed: {response.StatusCode} - {content}");

        var createdStation = await response.Content.ReadFromJsonAsync<Station>();
        Assert.That(createdStation, Is.Not.Null);
        Assert.That(createdStation.Id, Is.Not.Null.And.Not.Empty);
        Assert.That(createdStation.AccountId, Is.EqualTo(accountId));
        Assert.That(createdStation.Name, Is.EqualTo(station.Name));
        Assert.That(createdStation.Feeds, Is.EquivalentTo(station.Feeds));
    }

    [Test]
    public async Task GetStationsByAccountId_Works()
    {
        var accountId = Guid.NewGuid().ToString();
        
        // Create a station first
        var station = new Station { Name = "Station 1", Feeds = Array.Empty<string>() };
        var createRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/stations/{accountId}")
        {
            Content = JsonContent.Create(station)
        };
        createRequest.Headers.Add("X-API-KEY", _testApiKey);
        await _client.SendAsync(createRequest);

        // Get by account id
        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/stations/account/{accountId}");
        getRequest.Headers.Add("X-API-KEY", _testApiKey);

        var response = await _client.SendAsync(getRequest);
        Assert.That(response.IsSuccessStatusCode);

        var dtos = await response.Content.ReadFromJsonAsync<List<StationWithFeedsDto>>();
        Assert.That(dtos, Is.Not.Null);
        Assert.That(dtos.Count, Is.GreaterThan(0));
        Assert.That(dtos.Any(s => s.Station.Name == "Station 1"));
    }

    [Test]
    public async Task GetStationById_Works()
    {
        var accountId = Guid.NewGuid().ToString();
        var station = new Station { Name = "Station By Id" };
        var createRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/stations/{accountId}")
        {
            Content = JsonContent.Create(station)
        };
        createRequest.Headers.Add("X-API-KEY", _testApiKey);
        var createRes = await _client.SendAsync(createRequest);
        var createdStation = await createRes.Content.ReadFromJsonAsync<Station>();

        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/stations/{createdStation.Id}");
        getRequest.Headers.Add("X-API-KEY", _testApiKey);

        var response = await _client.SendAsync(getRequest);
        Assert.That(response.IsSuccessStatusCode, Is.True);

        var fetchedStation = await response.Content.ReadFromJsonAsync<Station>();
        Assert.That(fetchedStation, Is.Not.Null);
        Assert.That(fetchedStation.Id, Is.EqualTo(createdStation.Id));
        Assert.That(fetchedStation.Name, Is.EqualTo("Station By Id"));
    }

    [Test]
    public async Task UpdateStation_Works()
    {
        var accountId = Guid.NewGuid().ToString();
        var station = new Station { Name = "Original Name", Feeds = Array.Empty<string>() };
        
        // Create
        var createRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/stations/{accountId}")
        {
            Content = JsonContent.Create(station)
        };
        createRequest.Headers.Add("X-API-KEY", _testApiKey);
        var createRes = await _client.SendAsync(createRequest);
        var createdStation = await createRes.Content.ReadFromJsonAsync<Station>();

        // Update
        createdStation.Name = "Updated Name";
        var updateRequest = new HttpRequestMessage(HttpMethod.Put, "/api/stations")
        {
            Content = JsonContent.Create(createdStation)
        };
        updateRequest.Headers.Add("X-API-KEY", _testApiKey);
        
        var updateRes = await _client.SendAsync(updateRequest);
        Assert.That(updateRes.IsSuccessStatusCode);

        var updatedStation = await updateRes.Content.ReadFromJsonAsync<Station>();
        Assert.That(updatedStation, Is.Not.Null);
        Assert.That(updatedStation.Name, Is.EqualTo("Updated Name"));
    }

    [Test]
    public async Task DeleteStation_Works()
    {
        var accountId = Guid.NewGuid().ToString();
        var station = new Station { Name = "To Delete" };
        
        // Create
        var createRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/stations/{accountId}")
        {
            Content = JsonContent.Create(station)
        };
        createRequest.Headers.Add("X-API-KEY", _testApiKey);
        var createRes = await _client.SendAsync(createRequest);
        var createdStation = await createRes.Content.ReadFromJsonAsync<Station>();

        // Delete
        var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/stations/{createdStation.Id}");
        deleteRequest.Headers.Add("X-API-KEY", _testApiKey);

        var deleteRes = await _client.SendAsync(deleteRequest);
        Assert.That(deleteRes.IsSuccessStatusCode);

        // Verify it's deleted (fetch all for account)
        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/stations/account/{accountId}");
        getRequest.Headers.Add("X-API-KEY", _testApiKey);
        
        var getRes = await _client.SendAsync(getRequest);
        var stations = await getRes.Content.ReadFromJsonAsync<List<StationWithFeedsDto>>();
        Assert.That(stations.Any(s => s.Station.Id == createdStation.Id), Is.False);
    }

    [Test]
    public async Task DeleteStation_ClearsLastActiveEpisode_WhenStationWasActive()
    {
        var accountId = Guid.NewGuid().ToString();
        var station = new Station { Name = "To Delete", Feeds = Array.Empty<string>() };

        // Create
        var createRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/stations/{accountId}")
        {
            Content = JsonContent.Create(station)
        };
        createRequest.Headers.Add("X-API-KEY", _testApiKey);
        var createRes = await _client.SendAsync(createRequest);
        var createdStation = await createRes.Content.ReadFromJsonAsync<Station>();

        // Seed a profile that was actively listening to the station
        var profilesCol = _app.MongoDbContext.GetCollection<Profile>("ProfileCollection");
        var profile = new Profile
        {
            AccountId = accountId,
            AllowedStationIds = new[] { createdStation.Id },
            LastActiveStationId = createdStation.Id,
            LastActiveEpisodeId = Guid.NewGuid().ToString()
        };
        await profilesCol.InsertOneAsync(profile);

        // Delete
        var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/stations/{createdStation.Id}");
        deleteRequest.Headers.Add("X-API-KEY", _testApiKey);
        var deleteRes = await _client.SendAsync(deleteRequest);
        Assert.That(deleteRes.IsSuccessStatusCode);

        // All references to the deleted station (including the active episode) must be cleared
        var updatedProfile = await profilesCol.Find(p => p.Id == profile.Id).FirstOrDefaultAsync();
        Assert.That(updatedProfile, Is.Not.Null);
        Assert.That(updatedProfile.AllowedStationIds, Does.Not.Contain(createdStation.Id));
        Assert.That(updatedProfile.LastActiveStationId, Is.Null);
        Assert.That(updatedProfile.LastActiveEpisodeId, Is.Null);
    }

    [Test]
    public async Task UpdateSchedule_UpdatesScheduleSuccessfully()
    {
        var accountId = Guid.NewGuid().ToString();
        var station = new Station { Name = "To Schedule" };
        var createRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/stations/{accountId}")
        {
            Content = JsonContent.Create(station)
        };
        createRequest.Headers.Add("X-API-KEY", _testApiKey);
        var createRes = await _client.SendAsync(createRequest);
        var createdStation = await createRes.Content.ReadFromJsonAsync<Station>();
        
        var newSchedule = new StationSchedule();
        newSchedule.Feeds.Add(new StationFeedSchedule { Url = "https://www.spreaker.com/show/7042610/episodes/feed", SortOrder = FeedSortOrder.NewestToOldest });
        
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/stations/{createdStation.Id}/schedule");
        request.Headers.Add("X-API-KEY", _testApiKey);
        request.Content = JsonContent.Create(newSchedule);
        
        var result = await _client.SendAsync(request);
        result.EnsureSuccessStatusCode();
        
        var updated = await _app.MongoDbContext.GetCollection<Station>("StationCollection").Find(s => s.Id == createdStation.Id).FirstOrDefaultAsync();
        Assert.That(updated.Schedule.Feeds.Count, Is.EqualTo(1));
        Assert.That(updated.Schedule.Feeds[0].SortOrder, Is.EqualTo(FeedSortOrder.NewestToOldest));
    }

    [Test]
    public async Task GetPlaylist_WithNullLastActiveEpisodeId_FallsBackToFirstEpisode()
    {
        var accountId = Guid.NewGuid().ToString();
        var stationId = Guid.NewGuid().ToString();
        var profileId = Guid.NewGuid().ToString();

        var stationsService = _app.Services.GetRequiredService<Ohrbob.Api.Services.StationsService>();
        var profilesService = _app.Services.GetRequiredService<Ohrbob.Api.Services.ProfilesService>();
        var episodesService = _app.Services.GetRequiredService<Ohrbob.Api.Services.EpisodesService>();

        var station = new Station 
        { 
            Id = stationId,
            Name = "Fallback Test Station", 
            AccountId = accountId, 
            Feeds = new[] { "https://test.feed/url" } 
        };
        var createdStation = await stationsService.CreateAsync(station);

        var profile = new Profile
        {
            Id = profileId,
            AccountId = accountId,
            LastActiveStationId = createdStation.Id,
            LastActiveEpisodeId = null
        };
        var createdProfile = await profilesService.CreateAsync(profile);

        var episode = new Episode
        {
            Guid = "guid-123",
            FeedId = "feed-123",
            FeedUrl = "https://test.feed/url",
            Title = "Test Episode",
            PublishedAt = DateTime.UtcNow
        };
        await episodesService.CreateAsync(episode);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/stations/{createdStation.Id}/playlist?profileId={createdProfile.Id}");
        request.Headers.Add("X-API-KEY", _testApiKey);

        var res = await _client.SendAsync(request);
        var body = await res.Content.ReadAsStringAsync();
        Console.WriteLine($"Response Status: {res.StatusCode}");
        Console.WriteLine($"Response Body: {body}");
        Assert.That(res.IsSuccessStatusCode, Is.True);

        var playlist = await res.Content.ReadFromJsonAsync<List<EpisodePlayStateDto>>();
        Assert.That(playlist, Is.Not.Null);
        Assert.That(playlist.Count, Is.EqualTo(1));
        Assert.That(playlist[0].Episode.Title, Is.EqualTo("Test Episode"));
        Assert.That(playlist[0].IsActive, Is.True);
    }

    [Test]
    public async Task GetPlaylist_ReturnsAllEpisodes_WithoutTruncation()
    {
        var accountId = Guid.NewGuid().ToString();
        var stationId = Guid.NewGuid().ToString();
        var profileId = Guid.NewGuid().ToString();

        var stationsService = _app.Services.GetRequiredService<Ohrbob.Api.Services.StationsService>();
        var profilesService = _app.Services.GetRequiredService<Ohrbob.Api.Services.ProfilesService>();
        var episodesService = _app.Services.GetRequiredService<Ohrbob.Api.Services.EpisodesService>();

        var feedUrl = "https://test.feed/large";
        var station = new Station
        {
            Id = stationId,
            Name = "Large Station",
            AccountId = accountId,
            Feeds = new[] { feedUrl }
        };
        var createdStation = await stationsService.CreateAsync(station);

        var profile = new Profile
        {
            Id = profileId,
            AccountId = accountId,
            LastActiveStationId = createdStation.Id,
            LastActiveEpisodeId = null
        };
        var createdProfile = await profilesService.CreateAsync(profile);

        // A feed with well over the old 50-item cap. The Edit view must show every episode.
        const int episodeCount = 120;
        var baseDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < episodeCount; i++)
        {
            await episodesService.CreateAsync(new Episode
            {
                Guid = $"large-guid-{i:D3}",
                FeedId = "large-feed",
                FeedUrl = feedUrl,
                Title = $"Episode {i:D3}",
                PublishedAt = baseDate.AddDays(i)
            });
        }

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/stations/{createdStation.Id}/playlist?profileId={createdProfile.Id}");
        request.Headers.Add("X-API-KEY", _testApiKey);

        var res = await _client.SendAsync(request);
        Assert.That(res.IsSuccessStatusCode, Is.True);

        var playlist = await res.Content.ReadFromJsonAsync<List<EpisodePlayStateDto>>();
        Assert.That(playlist, Is.Not.Null);
        Assert.That(playlist!.Count, Is.EqualTo(episodeCount),
            "The playlist must contain every episode, not a truncated window");

        // Default order is oldest -> newest.
        Assert.That(playlist[0].Episode.Title, Is.EqualTo("Episode 000"));
        Assert.That(playlist[^1].Episode.Title, Is.EqualTo("Episode 119"));
    }

    [Test]
    public async Task GetCompletedPlaylist_Works()
    {
        var accountId = Guid.NewGuid().ToString();
        var stationId = Guid.NewGuid().ToString();
        var profileId = Guid.NewGuid().ToString();

        var stationsService = _app.Services.GetRequiredService<Ohrbob.Api.Services.StationsService>();
        var profilesService = _app.Services.GetRequiredService<Ohrbob.Api.Services.ProfilesService>();
        var episodesService = _app.Services.GetRequiredService<Ohrbob.Api.Services.EpisodesService>();
        var listenStateService = _app.Services.GetRequiredService<Ohrbob.Api.Services.ListenStateService>();

        var station = new Station 
        { 
            Id = stationId,
            Name = "Completed Playlist Test Station", 
            AccountId = accountId, 
            Feeds = new[] { "https://test.completed.feed/url" } 
        };
        var createdStation = await stationsService.CreateAsync(station);

        var profile = new Profile
        {
            Id = profileId,
            AccountId = accountId,
            LastActiveStationId = createdStation.Id,
            LastActiveEpisodeId = null
        };
        var createdProfile = await profilesService.CreateAsync(profile);

        var episode1 = new Episode
        {
            Id = Guid.NewGuid().ToString(),
            Guid = "guid-c1",
            FeedId = "feed-c1",
            FeedUrl = "https://test.completed.feed/url",
            Title = "Completed Episode",
            PublishedAt = DateTime.UtcNow.AddMinutes(-5)
        };
        var episode2 = new Episode
        {
            Id = Guid.NewGuid().ToString(),
            Guid = "guid-c2",
            FeedId = "feed-c1",
            FeedUrl = "https://test.completed.feed/url",
            Title = "Uncompleted Episode",
            PublishedAt = DateTime.UtcNow
        };
        await episodesService.CreateAsync(episode1);
        await episodesService.CreateAsync(episode2);

        var listenState = new ListenState
        {
            UserId = createdProfile.Id,
            EpisodeId = episode1.Id,
            StationId = stationId,
            IsListened = true
        };
        await listenStateService.CreateAsync(listenState);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/stations/{createdStation.Id}/completed?profileId={createdProfile.Id}");
        request.Headers.Add("X-API-KEY", _testApiKey);

        var res = await _client.SendAsync(request);
        Assert.That(res.IsSuccessStatusCode, Is.True);

        var completedList = await res.Content.ReadFromJsonAsync<List<EpisodePlayStateDto>>();
        Assert.That(completedList, Is.Not.Null);
        Assert.That(completedList.Count, Is.EqualTo(1));
        Assert.That(completedList[0].Episode.Id, Is.EqualTo(episode1.Id));
        Assert.That(completedList[0].Episode.Title, Is.EqualTo("Completed Episode"));
    }

    // -------------------------------------------------------------------------------------
    // Episodes left to play per profile ("nothing left to play" badge, issue #6)
    // -------------------------------------------------------------------------------------

    [Test]
    public async Task GetStationsByAccountId_WithProfileId_ReportsEpisodesLeftToPlay()
    {
        var accountId = Guid.NewGuid().ToString();
        var feedUrl = "https://test.unplayed.feed/url";

        var stationsService = _app.Services.GetRequiredService<Ohrbob.Api.Services.StationsService>();
        var profilesService = _app.Services.GetRequiredService<Ohrbob.Api.Services.ProfilesService>();
        var episodesService = _app.Services.GetRequiredService<Ohrbob.Api.Services.EpisodesService>();
        var listenStateService = _app.Services.GetRequiredService<Ohrbob.Api.Services.ListenStateService>();

        var station = await stationsService.CreateAsync(new Station
        {
            Name = "Unplayed Station",
            AccountId = accountId,
            Feeds = new[] { feedUrl }
        });
        var profile = await profilesService.CreateAsync(new Profile { AccountId = accountId });

        var ep1 = await episodesService.CreateAsync(new Episode
        {
            FeedId = "f1", FeedUrl = feedUrl, Title = "Episode 1", PublishedAt = DateTime.UtcNow.AddHours(-1)
        });
        var ep2 = await episodesService.CreateAsync(new Episode
        {
            FeedId = "f1", FeedUrl = feedUrl, Title = "Episode 2", PublishedAt = DateTime.UtcNow
        });

        Assert.That((await GetStationDtoAsync(accountId, profile.Id)).UnplayedEpisodeCount, Is.EqualTo(2),
            "Both episodes are unplayed for a fresh profile");

        await listenStateService.CreateAsync(new ListenState
        {
            UserId = profile.Id, StationId = station.Id, EpisodeId = ep1.Id, IsListened = true
        });
        Assert.That((await GetStationDtoAsync(accountId, profile.Id)).UnplayedEpisodeCount, Is.EqualTo(1),
            "Completing an episode must reduce the number of episodes left to play");

        await listenStateService.CreateAsync(new ListenState
        {
            UserId = profile.Id, StationId = station.Id, EpisodeId = ep2.Id, IsListened = true
        });
        Assert.That((await GetStationDtoAsync(accountId, profile.Id)).UnplayedEpisodeCount, Is.EqualTo(0),
            "When every episode is completed nothing is left to play");
    }

    [Test]
    public async Task GetStationsByAccountId_WithoutProfileId_LeavesEpisodesLeftToPlayUnset()
    {
        var accountId = Guid.NewGuid().ToString();
        var feedUrl = "https://test.unplayed.feed/noprofile";

        var stationsService = _app.Services.GetRequiredService<Ohrbob.Api.Services.StationsService>();
        var episodesService = _app.Services.GetRequiredService<Ohrbob.Api.Services.EpisodesService>();

        await stationsService.CreateAsync(new Station
        {
            Name = "No Profile Station",
            AccountId = accountId,
            Feeds = new[] { feedUrl }
        });
        await episodesService.CreateAsync(new Episode
        {
            FeedId = "f-np", FeedUrl = feedUrl, Title = "Episode", PublishedAt = DateTime.UtcNow
        });

        Assert.That((await GetStationDtoAsync(accountId, null)).UnplayedEpisodeCount, Is.Null,
            "Completion is per-profile, so the count can only be reported when a profileId is supplied");
    }

    [Test]
    public async Task GetStationsByAccountId_WithProfileId_RespectsStationAging()
    {
        var accountId = Guid.NewGuid().ToString();
        var feedUrl = "https://test.unplayed.feed/aging";

        var stationsService = _app.Services.GetRequiredService<Ohrbob.Api.Services.StationsService>();
        var profilesService = _app.Services.GetRequiredService<Ohrbob.Api.Services.ProfilesService>();
        var episodesService = _app.Services.GetRequiredService<Ohrbob.Api.Services.EpisodesService>();

        await stationsService.CreateAsync(new Station
        {
            Name = "Aging Station",
            AccountId = accountId,
            Feeds = new[] { feedUrl },
            IsAging = true
        });
        var profile = await profilesService.CreateAsync(new Profile { AccountId = accountId });

        for (var i = 0; i < 3; i++)
        {
            await episodesService.CreateAsync(new Episode
            {
                FeedId = "f-aging", FeedUrl = feedUrl, Title = $"Episode {i}", PublishedAt = DateTime.UtcNow.AddDays(i)
            });
        }

        Assert.That((await GetStationDtoAsync(accountId, profile.Id)).UnplayedEpisodeCount, Is.EqualTo(1),
            "With aging enabled only the newest episode of a feed is left to play");
    }

    [Test]
    public async Task SetEpisodePlayhead_CreatesStateWhenMissing()
    {
        var accountId = Guid.NewGuid().ToString();
        var feedUrl = "https://test.playhead.feed/new";

        var stationsService = _app.Services.GetRequiredService<Ohrbob.Api.Services.StationsService>();
        var profilesService = _app.Services.GetRequiredService<Ohrbob.Api.Services.ProfilesService>();
        var episodesService = _app.Services.GetRequiredService<Ohrbob.Api.Services.EpisodesService>();
        var listenStateService = _app.Services.GetRequiredService<Ohrbob.Api.Services.ListenStateService>();

        var station = await stationsService.CreateAsync(new Station
        {
            Name = "Playhead Station",
            AccountId = accountId,
            Feeds = new[] { feedUrl }
        });
        var profile = await profilesService.CreateAsync(new Profile { AccountId = accountId });
        var episode = await episodesService.CreateAsync(new Episode
        {
            FeedId = "f-playhead", FeedUrl = feedUrl, Title = "Playhead Episode", PublishedAt = DateTime.UtcNow
        });

        var response = await SetPlayheadAsync(station.Id, episode.Id, profile.Id, 77);
        Assert.That(response.IsSuccessStatusCode, Is.True);

        var state = await listenStateService.GetStateAsync(profile.Id, episode.Id);
        Assert.That(state, Is.Not.Null);
        Assert.That(state!.PositionSeconds, Is.EqualTo(77));
        Assert.That(state.StationId, Is.EqualTo(station.Id));
        Assert.That(state.IsListened, Is.False);
    }

    [Test]
    public async Task SetEpisodePlayhead_UpdatesExistingStateWithoutChangingListenedFlag()
    {
        var accountId = Guid.NewGuid().ToString();
        var feedUrl = "https://test.playhead.feed/existing";

        var stationsService = _app.Services.GetRequiredService<Ohrbob.Api.Services.StationsService>();
        var profilesService = _app.Services.GetRequiredService<Ohrbob.Api.Services.ProfilesService>();
        var episodesService = _app.Services.GetRequiredService<Ohrbob.Api.Services.EpisodesService>();
        var listenStateService = _app.Services.GetRequiredService<Ohrbob.Api.Services.ListenStateService>();

        var station = await stationsService.CreateAsync(new Station
        {
            Name = "Existing Playhead Station",
            AccountId = accountId,
            Feeds = new[] { feedUrl }
        });
        var profile = await profilesService.CreateAsync(new Profile { AccountId = accountId });
        var episode = await episodesService.CreateAsync(new Episode
        {
            FeedId = "f-playhead-2", FeedUrl = feedUrl, Title = "Existing Playhead Episode", PublishedAt = DateTime.UtcNow
        });

        await listenStateService.CreateAsync(new ListenState
        {
            UserId = profile.Id,
            EpisodeId = episode.Id,
            StationId = station.Id,
            PositionSeconds = 10,
            IsListened = true
        });

        var response = await SetPlayheadAsync(station.Id, episode.Id, profile.Id, 42);
        Assert.That(response.IsSuccessStatusCode, Is.True);

        var state = await listenStateService.GetStateAsync(profile.Id, episode.Id);
        Assert.That(state, Is.Not.Null);
        Assert.That(state!.PositionSeconds, Is.EqualTo(42));
        Assert.That(state.IsListened, Is.True, "Changing the playhead must not change the completed state");
    }

    [Test]
    public async Task SetEpisodePlayhead_ClampsNegativePositionToZero()
    {
        var accountId = Guid.NewGuid().ToString();
        var feedUrl = "https://test.playhead.feed/negative";

        var stationsService = _app.Services.GetRequiredService<Ohrbob.Api.Services.StationsService>();
        var profilesService = _app.Services.GetRequiredService<Ohrbob.Api.Services.ProfilesService>();
        var episodesService = _app.Services.GetRequiredService<Ohrbob.Api.Services.EpisodesService>();
        var listenStateService = _app.Services.GetRequiredService<Ohrbob.Api.Services.ListenStateService>();

        var station = await stationsService.CreateAsync(new Station
        {
            Name = "Negative Playhead Station",
            AccountId = accountId,
            Feeds = new[] { feedUrl }
        });
        var profile = await profilesService.CreateAsync(new Profile { AccountId = accountId });
        var episode = await episodesService.CreateAsync(new Episode
        {
            FeedId = "f-playhead-3", FeedUrl = feedUrl, Title = "Negative Playhead Episode", PublishedAt = DateTime.UtcNow
        });

        var response = await SetPlayheadAsync(station.Id, episode.Id, profile.Id, -30);
        Assert.That(response.IsSuccessStatusCode, Is.True);

        var state = await listenStateService.GetStateAsync(profile.Id, episode.Id);
        Assert.That(state, Is.Not.Null);
        Assert.That(state!.PositionSeconds, Is.EqualTo(0));
    }

    [Test]
    public async Task CreateStation_AssignsShareCode()
    {
        var accountId = Guid.NewGuid().ToString();
        var station = new Station { Name = "Shareable", Feeds = Array.Empty<string>() };

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/stations/{accountId}")
        {
            Content = JsonContent.Create(station)
        };
        request.Headers.Add("X-API-KEY", _testApiKey);

        var response = await _client.SendAsync(request);
        var createdStation = await response.Content.ReadFromJsonAsync<Station>();

        Assert.That(createdStation!.ShareCode, Has.Length.EqualTo(10));
        Assert.That(createdStation.ShareCode, Does.Match("^[A-Za-z0-9]{10}$"));
    }

    [Test]
    public async Task UpdateStation_WithEmptyShareCode_PreservesExistingCode()
    {
        var accountId = Guid.NewGuid().ToString();
        var station = new Station { Name = "Keep Code", Feeds = Array.Empty<string>() };
        var createRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/stations/{accountId}")
        {
            Content = JsonContent.Create(station)
        };
        createRequest.Headers.Add("X-API-KEY", _testApiKey);
        var createdStation = await (await _client.SendAsync(createRequest)).Content.ReadFromJsonAsync<Station>();
        var originalCode = createdStation!.ShareCode;

        createdStation.Name = "Renamed";
        createdStation.ShareCode = "Different1";

        var updateRequest = new HttpRequestMessage(HttpMethod.Put, "/api/stations")
        {
            Content = JsonContent.Create(createdStation)
        };
        updateRequest.Headers.Add("X-API-KEY", _testApiKey);

        var response = await _client.SendAsync(updateRequest);
        var updatedStation = await response.Content.ReadFromJsonAsync<Station>();

        Assert.That(updatedStation!.ShareCode, Is.EqualTo(originalCode));
    }

    [Test]
    public async Task GetStationByShareCode_ReturnsPublicStation()
    {
        var accountId = Guid.NewGuid().ToString();
        var station = new Station { Name = "Public Station", IsPublic = true, Feeds = Array.Empty<string>() };
        var createRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/stations/{accountId}")
        {
            Content = JsonContent.Create(station)
        };
        createRequest.Headers.Add("X-API-KEY", _testApiKey);
        var createdStation = await (await _client.SendAsync(createRequest)).Content.ReadFromJsonAsync<Station>();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/stations/share/{createdStation!.ShareCode}");
        request.Headers.Add("X-API-KEY", _testApiKey);

        var response = await _client.SendAsync(request);

        Assert.That(response.IsSuccessStatusCode, Is.True);
        var dto = await response.Content.ReadFromJsonAsync<StationWithFeedsDto>();
        Assert.That(dto!.Station.Id, Is.EqualTo(createdStation.Id));
    }

    [Test]
    public async Task GetStationByShareCode_ReturnsNotFound_WhenPrivate()
    {
        var accountId = Guid.NewGuid().ToString();
        var station = new Station { Name = "Private Station", IsPublic = false, Feeds = Array.Empty<string>() };
        var createRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/stations/{accountId}")
        {
            Content = JsonContent.Create(station)
        };
        createRequest.Headers.Add("X-API-KEY", _testApiKey);
        var createdStation = await (await _client.SendAsync(createRequest)).Content.ReadFromJsonAsync<Station>();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/stations/share/{createdStation!.ShareCode}");
        request.Headers.Add("X-API-KEY", _testApiKey);

        var response = await _client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetStationByShareCode_ReturnsNotFound_WhenDeleted()
    {
        var accountId = Guid.NewGuid().ToString();
        var station = new Station { Name = "Deleted Station", IsPublic = true, Feeds = Array.Empty<string>() };
        var createRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/stations/{accountId}")
        {
            Content = JsonContent.Create(station)
        };
        createRequest.Headers.Add("X-API-KEY", _testApiKey);
        var createdStation = await (await _client.SendAsync(createRequest)).Content.ReadFromJsonAsync<Station>();

        var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/stations/{createdStation!.Id}");
        deleteRequest.Headers.Add("X-API-KEY", _testApiKey);
        await _client.SendAsync(deleteRequest);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/stations/share/{createdStation.ShareCode}");
        request.Headers.Add("X-API-KEY", _testApiKey);

        var response = await _client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetStationDetails_WithProfileId_SetsUnplayedEpisodeCount()
    {
        var accountId = Guid.NewGuid().ToString();
        var feedUrl = "https://test.details.feed/url";
        var stationsService = _app.Services.GetRequiredService<Ohrbob.Api.Services.StationsService>();
        var profilesService = _app.Services.GetRequiredService<Ohrbob.Api.Services.ProfilesService>();
        var episodesService = _app.Services.GetRequiredService<Ohrbob.Api.Services.EpisodesService>();

        var station = await stationsService.CreateAsync(new Station
        {
            Name = "Details Station",
            AccountId = accountId,
            Feeds = new[] { feedUrl }
        });
        // The profile must not own the station: subscribed profiles use this endpoint too.
        var profile = await profilesService.CreateAsync(new Profile { AccountId = Guid.NewGuid().ToString() });
        await episodesService.CreateAsync(new Episode
        {
            FeedId = "f-details", FeedUrl = feedUrl, Title = "Episode", PublishedAt = DateTime.UtcNow
        });

        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/stations/{station!.Id}/details?profileId={profile!.Id}");
        request.Headers.Add("X-API-KEY", _testApiKey);

        var response = await _client.SendAsync(request);
        var dto = await response.Content.ReadFromJsonAsync<StationWithFeedsDto>();

        Assert.That(dto!.UnplayedEpisodeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task GetDetails_HostPacedSubscriber_OnlyCountsHostCompletedEpisodes()
    {
        var ownerAccountId = Guid.NewGuid().ToString();
        var subscriberAccountId = Guid.NewGuid().ToString();
        var stationsService = _app.Services.GetRequiredService<Ohrbob.Api.Services.StationsService>();
        var profilesService = _app.Services.GetRequiredService<Ohrbob.Api.Services.ProfilesService>();
        var episodesService = _app.Services.GetRequiredService<Ohrbob.Api.Services.EpisodesService>();
        var listenStateService = _app.Services.GetRequiredService<Ohrbob.Api.Services.ListenStateService>();

        var host = await profilesService.CreateAsync(new Profile { AccountId = ownerAccountId, Name = "Host", StreamToken = Guid.NewGuid().ToString() });
        var subscriber = await profilesService.CreateAsync(new Profile { AccountId = subscriberAccountId, Name = "Subscriber", StreamToken = Guid.NewGuid().ToString() });
        var station = await stationsService.CreateAsync(new Station
        {
            AccountId = ownerAccountId, Name = "Host Paced", Feeds = new[] { "https://host.pace/feed" },
            HostPaceEnabled = true, HostProfileId = host!.Id
        });
        var ep1 = await episodesService.CreateAsync(new Episode { FeedUrl = "https://host.pace/feed", Title = "Released", PublishedAt = DateTime.UtcNow.AddDays(-2) });
        await episodesService.CreateAsync(new Episode { FeedUrl = "https://host.pace/feed", Title = "Not released", PublishedAt = DateTime.UtcNow.AddDays(-1) });
        await listenStateService.CreateAsync(new ListenState
        {
            UserId = host.Id, EpisodeId = ep1!.Id, StationId = station!.Id, IsListened = true
        });

        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/stations/{station.Id}/details?profileId={subscriber!.Id}");
        request.Headers.Add("X-API-KEY", _testApiKey);
        var response = await _client.SendAsync(request);
        var dto = await response.Content.ReadFromJsonAsync<StationWithFeedsDto>();

        Assert.That(dto!.UnplayedEpisodeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task MarkEpisodeCompleted_SubscriberCannotCompleteUnreleasedEpisode()
    {
        var ownerAccountId = Guid.NewGuid().ToString();
        var subscriberAccountId = Guid.NewGuid().ToString();
        var stationsService = _app.Services.GetRequiredService<Ohrbob.Api.Services.StationsService>();
        var profilesService = _app.Services.GetRequiredService<Ohrbob.Api.Services.ProfilesService>();
        var episodesService = _app.Services.GetRequiredService<Ohrbob.Api.Services.EpisodesService>();

        var host = await profilesService.CreateAsync(new Profile { AccountId = ownerAccountId, Name = "Host", StreamToken = Guid.NewGuid().ToString() });
        var subscriber = await profilesService.CreateAsync(new Profile { AccountId = subscriberAccountId, Name = "Subscriber", StreamToken = Guid.NewGuid().ToString() });
        var station = await stationsService.CreateAsync(new Station
        {
            AccountId = ownerAccountId, Name = "Host Paced", Feeds = new[] { "https://host.pace/forbid" },
            HostPaceEnabled = true, HostProfileId = host!.Id
        });
        var unreleased = await episodesService.CreateAsync(new Episode
        {
            FeedUrl = "https://host.pace/forbid", Title = "Unreleased", PublishedAt = DateTime.UtcNow
        });

        var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/stations/{station!.Id}/episodes/{unreleased!.Id}/complete?profileId={subscriber!.Id}");
        request.Headers.Add("X-API-KEY", _testApiKey);
        var response = await _client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task CreateStation_WithHostProfileFromAnotherAccount_ReturnsBadRequest()
    {
        var ownerAccountId = Guid.NewGuid().ToString();
        var profilesService = _app.Services.GetRequiredService<Ohrbob.Api.Services.ProfilesService>();
        var foreignProfile = await profilesService.CreateAsync(new Profile
        {
            AccountId = Guid.NewGuid().ToString(), Name = "Someone else", StreamToken = Guid.NewGuid().ToString()
        });

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/stations/{ownerAccountId}")
        {
            Content = JsonContent.Create(new Station
            {
                Name = "Invalid host", HostPaceEnabled = true, HostProfileId = foreignProfile!.Id
            })
        };
        request.Headers.Add("X-API-KEY", _testApiKey);
        var response = await _client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task DeleteStation_RemovesItFromEveryProfileSubscriptions()
    {
        var accountId = Guid.NewGuid().ToString();
        var station = new Station { Name = "Subscribed Station", Feeds = Array.Empty<string>() };
        var createRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/stations/{accountId}")
        {
            Content = JsonContent.Create(station)
        };
        createRequest.Headers.Add("X-API-KEY", _testApiKey);
        var createdStation = await (await _client.SendAsync(createRequest)).Content.ReadFromJsonAsync<Station>();

        var profilesCol = _app.MongoDbContext.GetCollection<Profile>("ProfileCollection");
        var subscribedProfile = new Profile
        {
            Id = Guid.NewGuid().ToString(),
            AccountId = Guid.NewGuid().ToString(),
            StreamToken = Guid.NewGuid().ToString(),
            SubscribedStationIds = new[] { createdStation!.Id }
        };
        var otherProfile = new Profile
        {
            Id = Guid.NewGuid().ToString(),
            AccountId = Guid.NewGuid().ToString(),
            StreamToken = Guid.NewGuid().ToString(),
            SubscribedStationIds = new[] { "other-station" }
        };
        await profilesCol.InsertManyAsync(new[] { subscribedProfile, otherProfile });

        var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/stations/{createdStation.Id}");
        deleteRequest.Headers.Add("X-API-KEY", _testApiKey);
        await _client.SendAsync(deleteRequest);

        var updated = await profilesCol.Find(p => p.Id == subscribedProfile.Id).FirstOrDefaultAsync();
        var untouched = await profilesCol.Find(p => p.Id == otherProfile.Id).FirstOrDefaultAsync();
        Assert.That(updated!.SubscribedStationIds, Does.Not.Contain(createdStation.Id));
        Assert.That(untouched!.SubscribedStationIds, Is.EquivalentTo(new[] { "other-station" }));
    }

    private async Task<HttpResponseMessage> SetPlayheadAsync(string stationId, string episodeId, string profileId, int positionSeconds)
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/stations/{stationId}/episodes/{episodeId}/playhead?profileId={profileId}&positionSeconds={positionSeconds}");
        request.Headers.Add("X-API-KEY", _testApiKey);
        return await _client.SendAsync(request);
    }

    private async Task<StationWithFeedsDto> GetStationDtoAsync(string accountId, string? profileId)
    {
        var url = $"/api/stations/account/{accountId}";
        if (!string.IsNullOrEmpty(profileId)) url += $"?profileId={profileId}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-API-KEY", _testApiKey);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var dtos = await response.Content.ReadFromJsonAsync<List<StationWithFeedsDto>>();
        Assert.That(dtos, Is.Not.Null.And.Not.Empty);
        return dtos!.Single();
    }
}
