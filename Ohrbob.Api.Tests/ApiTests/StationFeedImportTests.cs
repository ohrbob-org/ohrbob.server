using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Ohrbob.Api.Configuration;
using Ohrbob.Api.Services;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Server.Shared.Requests;

namespace Ohrbob.Api.Tests.ApiTests;

public class StationFeedImportTests
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
                MongoDbLogs = new MongoDbSettings() { ConnectionString = "mongodb://localhost:27017/TestsOhrbobLogs", DatabaseName = "TestsOhrbobLogs" },
                MongoDbData = new MongoDbSettings() { ConnectionString = "mongodb://localhost:27017/TestsOhrbobData", DatabaseName = "TestsOhrbobData" },
                ApiKeys = new[] { new {
                    Key = _testApiKey,
                    Name = "test",
                    Scopes = new[] { "all" } } }
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

    private async Task SeedFeedAsync(string url)
    {
        var feeds = _app.Services.GetRequiredService<FeedsService>();
        await feeds.CreateAsync(new Feed { Url = url, Title = url });
    }

    private async Task<Station> CreateStationAsync(string accountId, params string[] feedUrls)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/stations/{accountId}")
        {
            Content = JsonContent.Create(new Station { Name = "Import Station", Feeds = feedUrls })
        };
        request.Headers.Add("X-API-KEY", _testApiKey);
        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.That(response.IsSuccessStatusCode, $"Failed: {response.StatusCode} - {body}");
        return (await response.Content.ReadFromJsonAsync<Station>())!;
    }

    private async Task<HttpResponseMessage> ImportAsync(string stationId, params string[] urls)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/stations/{stationId}/feeds")
        {
            Content = JsonContent.Create(new FeedImportRequest { Urls = urls.ToList() })
        };
        request.Headers.Add("X-API-KEY", _testApiKey);
        return await _client.SendAsync(request);
    }

    private async Task<Station> ReloadStationAsync(string stationId)
    {
        return (await _app.MongoDbContext!.GetCollection<Station>("StationCollection")
            .Find(s => s.Id == stationId).FirstOrDefaultAsync())!;
    }

    [Test]
    public async Task AddFeeds_AddsNewFeeds_AndReportsAlreadyPresent()
    {
        var accountId = Guid.NewGuid().ToString();
        const string existing = "https://feeds.test/existing.xml";
        const string added = "https://feeds.test/new.xml";
        await SeedFeedAsync(existing);
        await SeedFeedAsync(added);

        var station = await CreateStationAsync(accountId, existing);

        var response = await ImportAsync(station.Id, existing, added);

        Assert.That(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<FeedImportResultDto>();
        Assert.That(result!.Added, Is.EqualTo(new[] { added }));
        Assert.That(result.AlreadyPresent, Is.EqualTo(new[] { existing }));

        var stored = await ReloadStationAsync(station.Id);
        Assert.That(stored.Schedule.Feeds.Select(f => f.Url), Is.EqualTo(new[] { existing, added }));
    }

    [Test]
    public async Task AddFeeds_SkipsUnreadableUrls_AndKeepsValidOnes()
    {
        var accountId = Guid.NewGuid().ToString();
        const string valid = "https://feeds.test/valid.xml";
        const string unreadable = "not-a-valid-url";
        await SeedFeedAsync(valid);

        var station = await CreateStationAsync(accountId);

        var response = await ImportAsync(station.Id, unreadable, valid);

        Assert.That(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<FeedImportResultDto>();
        Assert.That(result!.Invalid, Is.EqualTo(new[] { unreadable }));
        Assert.That(result.Added, Is.EqualTo(new[] { valid }));

        var stored = await ReloadStationAsync(station.Id);
        Assert.That(stored.Schedule.Feeds.Select(f => f.Url), Is.EqualTo(new[] { valid }));
    }

    [Test]
    public async Task AddFeeds_IgnoresDuplicateUrlsWithinTheRequest()
    {
        var accountId = Guid.NewGuid().ToString();
        const string url = "https://feeds.test/dupe.xml";
        await SeedFeedAsync(url);

        var station = await CreateStationAsync(accountId);

        var response = await ImportAsync(station.Id, url, url);

        Assert.That(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<FeedImportResultDto>();
        Assert.That(result!.Added, Is.EqualTo(new[] { url }));
        Assert.That(result.AlreadyPresent, Is.Empty);

        var stored = await ReloadStationAsync(station.Id);
        Assert.That(stored.Schedule.Feeds.Select(f => f.Url), Is.EqualTo(new[] { url }));
    }

    [Test]
    public async Task AddFeeds_StopsAtFreeTierFeedLimit()
    {
        var accountId = Guid.NewGuid().ToString();
        var existing = Enumerable.Range(0, 9).Select(i => $"https://feeds.test/limit-{i}.xml").ToList();
        foreach (var url in existing) await SeedFeedAsync(url);
        const string extra1 = "https://feeds.test/extra-1.xml";
        const string extra2 = "https://feeds.test/extra-2.xml";
        await SeedFeedAsync(extra1);
        await SeedFeedAsync(extra2);

        var station = await CreateStationAsync(accountId, existing.ToArray());

        var response = await ImportAsync(station.Id, extra1, extra2);

        Assert.That(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<FeedImportResultDto>();
        Assert.That(result!.Added, Is.EqualTo(new[] { extra1 }));
        Assert.That(result.NotAddedDueToLimit, Is.EqualTo(new[] { extra2 }));

        var stored = await ReloadStationAsync(station.Id);
        Assert.That(stored.Schedule.Feeds.Count, Is.EqualTo(10));
        Assert.That(stored.Schedule.Feeds.Select(f => f.Url), Does.Contain(extra1));
        Assert.That(stored.Schedule.Feeds.Select(f => f.Url), Does.Not.Contain(extra2));
    }

    [Test]
    public async Task AddFeeds_AddsAllFeeds_WhenTierIsUnlimited()
    {
        var accounts = _app.Services.GetRequiredService<AccountService>();
        var account = await accounts.CreateAsync(new Account
        {
            Email = $"unlimited-{Guid.NewGuid()}@test.com",
            Name = "Unlimited",
            Tier = SubscriptionTier.Morebob,
            NeedsOnboarding = false
        });
        var accountId = account!.Id;

        var existing = Enumerable.Range(0, 10).Select(i => $"https://feeds.test/unlimited-{i}.xml").ToList();
        foreach (var url in existing) await SeedFeedAsync(url);
        const string extra1 = "https://feeds.test/unlimited-extra-1.xml";
        const string extra2 = "https://feeds.test/unlimited-extra-2.xml";
        await SeedFeedAsync(extra1);
        await SeedFeedAsync(extra2);

        var station = await CreateStationAsync(accountId, existing.ToArray());

        var response = await ImportAsync(station.Id, extra1, extra2);

        Assert.That(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<FeedImportResultDto>();
        Assert.That(result!.Added, Is.EqualTo(new[] { extra1, extra2 }));
        Assert.That(result.NotAddedDueToLimit, Is.Empty);

        var stored = await ReloadStationAsync(station.Id);
        Assert.That(stored.Schedule.Feeds.Count, Is.EqualTo(12));
    }

    [Test]
    public async Task AddFeeds_UnknownStation_ReturnsNotFound()
    {
        var response = await ImportAsync(Guid.NewGuid().ToString(), "https://feeds.test/ghost.xml");

        Assert.That(response.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.NotFound));
    }

    [Test]
    public async Task AddFeeds_AssignsDistinctColorsToNewFeeds()
    {
        var accountId = Guid.NewGuid().ToString();
        const string first = "https://feeds.test/color-a.xml";
        const string second = "https://feeds.test/color-b.xml";
        await SeedFeedAsync(first);
        await SeedFeedAsync(second);

        var station = await CreateStationAsync(accountId, first);

        var response = await ImportAsync(station.Id, second);

        Assert.That(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        var stored = await ReloadStationAsync(station.Id);
        Assert.That(stored.Schedule.Feeds, Has.All.Matches<StationFeedSchedule>(f => !string.IsNullOrEmpty(f.Color)));
        Assert.That(stored.Schedule.Feeds.Select(f => f.Color).Distinct().Count(), Is.EqualTo(2));
    }
}
