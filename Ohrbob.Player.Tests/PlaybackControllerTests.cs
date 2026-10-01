using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Text.Encodings.Web;
using System.Security.Claims;
using Ohrbob.Server.Shared.Data;
using MongoDB.Driver;
using Ohrbob.Player.Configuration;
using Ohrbob.Player.Services;
using NUnit.Framework;

namespace Ohrbob.Player.Tests;

public class PlaybackControllerTests
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
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("TestScheme", options => { });
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

    [Test]
    public async Task GetStations_ReturnsAllowedStations()
    {
        var profile = new Profile { AccountId = "test-account", StreamToken = "token-123", AllowedStationIds = new[] { "s1", "s2" } };
        var profilesCol = _app.MongoDbContext.GetCollection<Profile>("ProfileCollection");
        await profilesCol.InsertOneAsync(profile);

        _client.DefaultRequestHeaders.Add("X-Stream-Token", "token-123");
        var response = await _client.GetAsync("/api/playback/stations");
        response.EnsureSuccessStatusCode();
        var stations = await response.Content.ReadFromJsonAsync<string[]>();
        
        Assert.That(stations, Is.Not.Null);
        Assert.That(stations.Length, Is.EqualTo(2));
        Assert.That(stations, Contains.Item("s1"));
    }

    [Test]
    public async Task StateEndpoints_GetAndSetState_Works()
    {
        var profile = new Profile { AccountId = "test-account", StreamToken = "token-123" };
        var profilesCol = _app.MongoDbContext.GetCollection<Profile>("ProfileCollection");
        await profilesCol.InsertOneAsync(profile);

        _client.DefaultRequestHeaders.Add("X-Stream-Token", "token-123");

        // Initial get
        var get1 = await _client.GetAsync("/api/playback/state/ep-1");
        get1.EnsureSuccessStatusCode();
        var state1 = await get1.Content.ReadFromJsonAsync<Controllers.SetStateRequest>();
        Assert.That(state1.PositionSeconds, Is.EqualTo(0));

        // Post new state
        var postReq = new Controllers.SetStateRequest { StationId = "s1", PositionSeconds = 42, IsListened = false };
        var postRes = await _client.PostAsJsonAsync("/api/playback/state/ep-1", postReq);
        postRes.EnsureSuccessStatusCode();

        // Second get
        var get2 = await _client.GetAsync("/api/playback/state/ep-1");
        get2.EnsureSuccessStatusCode();
        var state2 = await get2.Content.ReadFromJsonAsync<Controllers.SetStateRequest>();
        Assert.That(state2.PositionSeconds, Is.EqualTo(42));
    }

    public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder) { }
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[] { new Claim("ohrbob_account_id", "test-account") };
            var identity = new ClaimsIdentity(claims, "Test");
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, "TestScheme");
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
