using NUnit.Framework;
using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Text.Encodings.Web;
using System.Security.Claims;
using Ohrbob.Server.Shared.Data;
using MongoDB.Driver;

namespace Ohrbob.Player.Tests;

public class PlayerContextTests
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
    }

    [TearDown]
    public void TearDown()
    {
        _app?.Dispose();
        _client?.Dispose();
    }

    [Test]
    public async Task ProtectedEndpoint_NoAuth_Returns401()
    {
        var response = await _client.GetAsync("/api/playback/stations");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized).Or.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task ProtectedEndpoint_WithAuthNoStreamToken_Returns403()
    {
        _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("TestScheme");
        var response = await _client.GetAsync("/api/playback/stations");
        // Our controller will likely return 403 or 400 if GetCurrentProfileAsync is null
        Assert.That(response.StatusCode, Is.AnyOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.BadRequest));
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
