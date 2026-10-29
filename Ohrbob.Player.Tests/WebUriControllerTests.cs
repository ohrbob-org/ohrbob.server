using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using NUnit.Framework;
using Ohrbob.Player.Services;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Player.Tests;

public class WebUriControllerTests
{
    private const string TestWebUri = "https://web.test.local";
    private readonly string _databaseName = "TestDb_" + Guid.NewGuid().ToString();
    private ApiTestApp<Program> _app = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void Setup()
    {
        // Environment variables outrank the test app's in-memory config, so pin the value here.
        Environment.SetEnvironmentVariable("OhrbobPlayer__WebPublicUrl", TestWebUri);
        _app = new ApiTestApp<Program>(databaseName: _databaseName, setupMongoDbContext: true);
        _client = _app.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("OhrbobPlayer__WebPublicUrl", null);
        _client.Dispose();
        _app.Dispose();
    }

    private async Task<Profile> CreateProfileWithStreamToken()
    {
        var profile = new Profile { Id = Guid.NewGuid().ToString(), AccountId = "test-account", StreamToken = Guid.NewGuid().ToString() };
        await _app.MongoDbContext!.GetCollection<Profile>("ProfileCollection").InsertOneAsync(profile);
        return profile;
    }

    [Test]
    public async Task Get_WithProfileStreamToken_ReturnsConfiguredWebUri()
    {
        var profile = await CreateProfileWithStreamToken();
        _client.DefaultRequestHeaders.Add("X-Stream-Token", profile.StreamToken);

        var response = await _client.GetAsync("/weburi");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<Controllers.WebUriResponse>();
        Assert.That(body!.Uri, Is.EqualTo(TestWebUri));
    }

    [Test]
    public async Task Get_WithPlayerApiKey_ReturnsConfiguredWebUri()
    {
        var profile = await CreateProfileWithStreamToken();
        var apiKey = new ApiKey
        {
            AccountId = profile.AccountId,
            ProfileId = profile.Id,
            Name = "player",
            Token = Guid.NewGuid().ToString()
        };
        await _app.MongoDbContext!.GetCollection<ApiKey>("ApiKeyCollection").InsertOneAsync(apiKey);
        _client.DefaultRequestHeaders.Authorization = new("Bearer", apiKey.Token);

        var response = await _client.GetAsync("/weburi");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Get_WithoutKey_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/weburi");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Get_WithUnknownKey_ReturnsUnauthorized()
    {
        _client.DefaultRequestHeaders.Authorization = new("Bearer", Guid.NewGuid().ToString());

        var response = await _client.GetAsync("/weburi");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }
}
