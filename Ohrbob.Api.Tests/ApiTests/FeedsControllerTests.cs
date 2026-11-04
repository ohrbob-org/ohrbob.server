using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ohrbob.Api.Configuration;
using Ohrbob.Api.Services;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Tests.ApiTests;

public class FeedsControllerTests
{
    private ApiTestApp<Program> _app = null!;
    private HttpClient _client = null!;
    private const string TestApiKey = "123456";

    [SetUp]
    public void Setup()
    {
        _app = new ApiTestApp<Program>(serviceCollection => { }, configuration =>
        {
            var configContainer = new
            {
                MongoDbLogs = new MongoDbSettings { ConnectionString = "mongodb://localhost:27017/TestsOhrbobLogs", DatabaseName = "TestsOhrbobLogs" },
                MongoDbData = new MongoDbSettings { ConnectionString = "mongodb://localhost:27017/TestsOhrbobData", DatabaseName = "TestsOhrbobData" },
                ApiKeys = new[] { new { Key = TestApiKey, Name = "test", Scopes = new[] { "all" } } }
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
    public async Task GetAll_ReturnsStoredFeeds()
    {
        var feedsService = _app.Services.GetRequiredService<FeedsService>();
        var created = await feedsService.CreateAsync(new Feed
        {
            Title = "Comedy Show",
            Url = "https://example.com/comedy.xml",
            Category = "Comedy"
        });

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/feeds");
        request.Headers.Add("X-API-KEY", TestApiKey);

        var response = await _client.SendAsync(request);
        Assert.That(response.IsSuccessStatusCode, $"Failed: {response.StatusCode}");

        var feeds = await response.Content.ReadFromJsonAsync<List<Feed>>();
        Assert.That(feeds, Is.Not.Null);
        Assert.That(feeds!.Any(f => f.Id == created!.Id && f.Category == "Comedy"), Is.True);
    }

    [Test]
    public async Task GetAll_WithoutApiKey_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/feeds");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }
}
