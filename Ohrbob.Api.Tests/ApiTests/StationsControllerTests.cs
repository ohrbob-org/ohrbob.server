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
}
