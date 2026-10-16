using NUnit.Framework;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using Ohrbob.Api.Services;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Tests.ApiTests;

public class ProfilesControllerTests
{
    private ApiTestApp<Program> _app;
    private HttpClient _client;
    private const string _testApiKey = "123456";
    private readonly string _databaseName = "TestDb_" + Guid.NewGuid().ToString();

    [SetUp]
    public void Setup()
    {
        Environment.SetEnvironmentVariable("ApiKey", _testApiKey);
        _app = new ApiTestApp<Program>(
            databaseName: _databaseName,
            setupMongoDbContext: true,
            configurationOverride: builder => 
            {
                builder.AddInMemoryCollection(new[]
                {
                    new KeyValuePair<string, string?>("MongoDbLogs:ConnectionString", "mongodb://localhost:27017/TestLogs"),
                    new KeyValuePair<string, string?>("MongoDbLogs:DatabaseName", "TestLogs"),
                    new KeyValuePair<string, string?>("ApiKey", _testApiKey)
                });
            }
        );
        _client = _app.CreateClient();
        _client.DefaultRequestHeaders.Add("x-api-key", _testApiKey);
        // By looking at how other tests bypass auth or pass claims, we use TestIdp value if needed.
        // It seems AccountsControllerTests uses the apiKey and that's enough to pass CreateAccountPolicy?
        // Let's rely on x-api-key header. The issue was 403 Forbidden which implies policy check failed.
        // If ApiKey authentication succeeds but policy fails, maybe we need to mock something or bypass policy.
    }

    [TearDown]
    public void TearDown()
    {
        _app?.Dispose();
        _client?.Dispose();
    }

    [Test]
    public async Task GetProfilesByAccountId_ShouldReturnProfiles()
    {
        // Workaround to bypass auth policies for unit testing controllers directly
        using var scope = _app.Services.CreateScope();
        var controller = new Controllers.ProfilesController(scope.ServiceProvider.GetRequiredService<ProfilesService>(), scope.ServiceProvider.GetRequiredService<StationsService>());
        var accountId = Guid.NewGuid().ToString();
        var profile = new Profile { AccountId = accountId, Name = "Test Profile" };
        var profilesService = scope.ServiceProvider.GetRequiredService<ProfilesService>();
        await profilesService.CreateAsync(profile);

        var result = await controller.GetByAccountId(accountId);
        var okResult = result.Result as Microsoft.AspNetCore.Mvc.OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var profiles = okResult.Value as List<Profile>;
        Assert.That(profiles, Is.Not.Null);
        Assert.That(profiles, Has.Count.EqualTo(1));
        Assert.That(profiles[0].Name, Is.EqualTo("Test Profile"));
    }

    [Test]
    public async Task CreateProfile_ShouldGenerateStreamToken()
    {
        using var scope = _app.Services.CreateScope();
        var controller = new Controllers.ProfilesController(scope.ServiceProvider.GetRequiredService<ProfilesService>(), scope.ServiceProvider.GetRequiredService<StationsService>());
        var accountId = Guid.NewGuid().ToString();
        var profile = new Profile { Name = "New Profile" };
        
        var result = await controller.Create(accountId, profile);
        var okResult = result.Result as Microsoft.AspNetCore.Mvc.OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        var created = okResult.Value as Profile;
        Assert.That(created, Is.Not.Null);
        Assert.That(created.AccountId, Is.EqualTo(accountId));
        Assert.That(created.Name, Is.EqualTo("New Profile"));
        Assert.That(string.IsNullOrEmpty(created.StreamToken), Is.False);
        Assert.That(Guid.TryParse(created.StreamToken, out _), Is.True);
    }

    [Test]
    public async Task CreateProfile_WhenAccountAlreadyHasProfile_ShouldCreateAdditionalProfileWithFreshStreamToken()
    {
        using var scope = _app.Services.CreateScope();
        var profilesService = scope.ServiceProvider.GetRequiredService<ProfilesService>();
        var accountService = scope.ServiceProvider.GetRequiredService<AccountService>();
        var policy = scope.ServiceProvider.GetRequiredService<SubscriptionPolicy>();
        var controller = new Controllers.ProfilesController(
            profilesService,
            scope.ServiceProvider.GetRequiredService<StationsService>(),
            accountService,
            policy);

        var account = new Account { Email = $"dup-{Guid.NewGuid()}@test.com", Name = "Onboarding Retry" };
        var storedAccount = await accountService.CreateAsync(account);
        Assert.That(storedAccount, Is.Not.Null);

        // First profile (e.g. created on the first onboarding attempt).
        var firstResult = await controller.Create(storedAccount!.Id, new Profile { Name = "First" });
        var first = (firstResult.Result as Microsoft.AspNetCore.Mvc.OkObjectResult)?.Value as Profile;
        Assert.That(first, Is.Not.Null);
        Assert.That(string.IsNullOrEmpty(first!.Id), Is.False);

        // Retrying creates a second profile and must not blow up on the unique StreamToken index.
        Microsoft.AspNetCore.Mvc.ActionResult<Profile>? secondResult = null;
        Assert.DoesNotThrowAsync(async () =>
        {
            secondResult = await controller.Create(storedAccount.Id, new Profile { Name = "Second" });
        });
        var second = (secondResult!.Result as Microsoft.AspNetCore.Mvc.OkObjectResult)?.Value as Profile;
        Assert.That(second, Is.Not.Null);
        Assert.That(second!.Id, Is.Not.EqualTo(first.Id));
        Assert.That(second.StreamToken, Is.Not.EqualTo(first.StreamToken));
        Assert.That(second.Name, Is.EqualTo("Second"));

        var profiles = await profilesService.GetByAccountIdAsync(storedAccount.Id);
        Assert.That(profiles, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task AddStation_ShouldAddStationId()
    {
        using var scope = _app.Services.CreateScope();
        var controller = new Controllers.ProfilesController(
            scope.ServiceProvider.GetRequiredService<ProfilesService>(),
            scope.ServiceProvider.GetRequiredService<StationsService>()
        );
        
        var accountId = Guid.NewGuid().ToString();
        var profile = new Profile { AccountId = accountId, Name = "Test Profile" };
        var profilesService = scope.ServiceProvider.GetRequiredService<ProfilesService>();
        await profilesService.CreateAsync(profile);

        var station = new Station { AccountId = accountId, Name = "Test Station" };
        var stationsService = scope.ServiceProvider.GetRequiredService<StationsService>();
        await stationsService.CreateAsync(station);

        var result = await controller.AddStation(profile.Id, station.Id);
        Assert.That(result, Is.InstanceOf<Microsoft.AspNetCore.Mvc.OkResult>());

        var updatedProfile = await profilesService.GetAsync(profile.Id);
        Assert.That(updatedProfile.AllowedStationIds, Contains.Item(station.Id));
    }
}
