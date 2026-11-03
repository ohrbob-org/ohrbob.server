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
                    new KeyValuePair<string, string?>("ApiKey", _testApiKey),
                    new KeyValuePair<string, string?>("SubscriptionTiers:Morebob:MaxStations", "-1"),
                    new KeyValuePair<string, string?>("SubscriptionTiers:Morebob:MaxFeedsPerStation", "-1"),
                    new KeyValuePair<string, string?>("SubscriptionTiers:Free:MaxStations", "2"),
                    new KeyValuePair<string, string?>("SubscriptionTiers:Free:MaxFeedsPerStation", "10"),
                    new KeyValuePair<string, string?>("SubscriptionTiers:Free:MaxProfiles", "1"),
                    new KeyValuePair<string, string?>("SubscriptionTiers:Morebob:MaxProfiles", "5")
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

        var account = new Account { Email = $"dup-{Guid.NewGuid()}@test.com", Name = "Onboarding Retry", Tier = SubscriptionTier.Morebob };
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
    public async Task CreateProfile_WhenFreeTierLimitReached_ShouldReturnBadRequest()
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

        var account = new Account { Email = $"dup-{Guid.NewGuid()}@test.com", Name = "Limit Test Free", Tier = SubscriptionTier.Free };
        var storedAccount = await accountService.CreateAsync(account);

        // First profile (limit is 1 for free tier)
        var firstResult = await controller.Create(storedAccount!.Id, new Profile { Name = "First" });
        Assert.That(firstResult.Result, Is.InstanceOf<Microsoft.AspNetCore.Mvc.OkObjectResult>());

        // Second profile should fail
        var secondResult = await controller.Create(storedAccount.Id, new Profile { Name = "Second" });
        Assert.That(secondResult.Result, Is.InstanceOf<Microsoft.AspNetCore.Mvc.BadRequestObjectResult>());
        var badRequest = secondResult.Result as Microsoft.AspNetCore.Mvc.BadRequestObjectResult;
        Assert.That(badRequest!.Value, Is.EqualTo("Profile limit reached"));
    }

    [Test]
    public async Task CreateProfile_WhenMorebobTierLimitReached_ShouldReturnBadRequest()
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

        var account = new Account { Email = $"dup-{Guid.NewGuid()}@test.com", Name = "Limit Test Morebob", Tier = SubscriptionTier.Morebob };
        var storedAccount = await accountService.CreateAsync(account);

        // Create up to the limit (5 for Morebob)
        for (int i = 0; i < 5; i++)
        {
            var result = await controller.Create(storedAccount!.Id, new Profile { Name = $"Profile {i}" });
            Assert.That(result.Result, Is.InstanceOf<Microsoft.AspNetCore.Mvc.OkObjectResult>());
        }

        // 6th profile should fail
        var extraResult = await controller.Create(storedAccount!.Id, new Profile { Name = "Extra" });
        Assert.That(extraResult.Result, Is.InstanceOf<Microsoft.AspNetCore.Mvc.BadRequestObjectResult>());
        var badRequest = extraResult.Result as Microsoft.AspNetCore.Mvc.BadRequestObjectResult;
        Assert.That(badRequest!.Value, Is.EqualTo("Profile limit reached"));
    }

    [Test]
    public async Task DeleteProfile_WhenNotLastProfile_ShouldReturnOk()
    {
        using var scope = _app.Services.CreateScope();
        var profilesService = scope.ServiceProvider.GetRequiredService<ProfilesService>();
        var accountService = scope.ServiceProvider.GetRequiredService<AccountService>();
        var controller = new Controllers.ProfilesController(
            profilesService,
            scope.ServiceProvider.GetRequiredService<StationsService>(),
            accountService,
            scope.ServiceProvider.GetRequiredService<SubscriptionPolicy>());

        var accountId = Guid.NewGuid().ToString();
        var profile1 = new Profile { AccountId = accountId, Name = "Profile 1", StreamToken = "token1" };
        var profile2 = new Profile { AccountId = accountId, Name = "Profile 2", StreamToken = "token2" };
        
        await profilesService.CreateAsync(profile1);
        var p2Created = await profilesService.CreateAsync(profile2);

        var result = await controller.Delete(p2Created!.Id!);
        Assert.That(result, Is.InstanceOf<Microsoft.AspNetCore.Mvc.OkResult>());

        var profiles = await profilesService.GetByAccountIdAsync(accountId);
        Assert.That(profiles.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task DeleteProfile_WhenLastProfile_ShouldReturnBadRequest()
    {
        using var scope = _app.Services.CreateScope();
        var profilesService = scope.ServiceProvider.GetRequiredService<ProfilesService>();
        var accountService = scope.ServiceProvider.GetRequiredService<AccountService>();
        var controller = new Controllers.ProfilesController(
            profilesService,
            scope.ServiceProvider.GetRequiredService<StationsService>(),
            accountService,
            scope.ServiceProvider.GetRequiredService<SubscriptionPolicy>());

        var accountId = Guid.NewGuid().ToString();
        var profile = new Profile { AccountId = accountId, Name = "Only Profile", StreamToken = "token" };
        var created = await profilesService.CreateAsync(profile);

        var result = await controller.Delete(created!.Id!);
        
        Assert.That(result, Is.InstanceOf<Microsoft.AspNetCore.Mvc.BadRequestObjectResult>());
        var badRequest = result as Microsoft.AspNetCore.Mvc.BadRequestObjectResult;
        
        var problem = badRequest!.Value as Microsoft.AspNetCore.Mvc.ProblemDetails;
        Assert.That(problem, Is.Not.Null);
        Assert.That(problem!.Title, Is.EqualTo("Profile Delete Error"));
        
        var profiles = await profilesService.GetByAccountIdAsync(accountId);
        Assert.That(profiles.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task Subscribe_AddsStationToProfile()
    {
        using var scope = _app.Services.CreateScope();
        var profilesService = scope.ServiceProvider.GetRequiredService<ProfilesService>();
        var stationsService = scope.ServiceProvider.GetRequiredService<StationsService>();
        var controller = new Controllers.ProfilesController(profilesService, stationsService);

        var ownerAccountId = Guid.NewGuid().ToString();
        var subscriberAccountId = Guid.NewGuid().ToString();
        var profile = await profilesService.CreateAsync(new Profile { AccountId = subscriberAccountId, Name = "Subscriber" });
        var station = await stationsService.CreateAsync(new Station
        {
            AccountId = ownerAccountId, Name = "Public Station", IsPublic = true
        });

        var result = await controller.Subscribe(profile!.Id, station!.Id);

        Assert.That(result, Is.InstanceOf<Microsoft.AspNetCore.Mvc.OkResult>());
        var updated = await profilesService.GetAsync(profile.Id);
        Assert.That(updated!.SubscribedStationIds, Contains.Item(station.Id));
    }

    [Test]
    public async Task Subscribe_IsIdempotent()
    {
        using var scope = _app.Services.CreateScope();
        var profilesService = scope.ServiceProvider.GetRequiredService<ProfilesService>();
        var stationsService = scope.ServiceProvider.GetRequiredService<StationsService>();
        var controller = new Controllers.ProfilesController(profilesService, stationsService);

        var profile = await profilesService.CreateAsync(new Profile
        {
            AccountId = Guid.NewGuid().ToString(), Name = "Subscriber"
        });
        var station = await stationsService.CreateAsync(new Station
        {
            AccountId = Guid.NewGuid().ToString(), Name = "Public Station", IsPublic = true
        });

        await controller.Subscribe(profile!.Id, station!.Id);
        await controller.Subscribe(profile.Id, station.Id);

        var updated = await profilesService.GetAsync(profile.Id);
        Assert.That(updated!.SubscribedStationIds.Count(id => id == station.Id), Is.EqualTo(1));
    }

    [Test]
    public async Task Subscribe_PrivateStation_ReturnsForbidden()
    {
        using var scope = _app.Services.CreateScope();
        var profilesService = scope.ServiceProvider.GetRequiredService<ProfilesService>();
        var stationsService = scope.ServiceProvider.GetRequiredService<StationsService>();
        var controller = new Controllers.ProfilesController(profilesService, stationsService);

        var profile = await profilesService.CreateAsync(new Profile
        {
            AccountId = Guid.NewGuid().ToString(), Name = "Subscriber"
        });
        var station = await stationsService.CreateAsync(new Station
        {
            AccountId = Guid.NewGuid().ToString(), Name = "Private Station", IsPublic = false
        });

        var result = await controller.Subscribe(profile!.Id, station!.Id);

        Assert.That(result, Is.InstanceOf<Microsoft.AspNetCore.Mvc.ForbidResult>());
    }

    [Test]
    public async Task Subscribe_OwnStation_ReturnsForbidden()
    {
        using var scope = _app.Services.CreateScope();
        var profilesService = scope.ServiceProvider.GetRequiredService<ProfilesService>();
        var stationsService = scope.ServiceProvider.GetRequiredService<StationsService>();
        var controller = new Controllers.ProfilesController(profilesService, stationsService);

        var accountId = Guid.NewGuid().ToString();
        var profile = await profilesService.CreateAsync(new Profile { AccountId = accountId, Name = "Owner" });
        var station = await stationsService.CreateAsync(new Station
        {
            AccountId = accountId, Name = "Own Station", IsPublic = true
        });

        var result = await controller.Subscribe(profile!.Id, station!.Id);

        Assert.That(result, Is.InstanceOf<Microsoft.AspNetCore.Mvc.ForbidResult>());
    }

    [Test]
    public async Task Unsubscribe_RemovesStation_AndIsIdempotent()
    {
        using var scope = _app.Services.CreateScope();
        var profilesService = scope.ServiceProvider.GetRequiredService<ProfilesService>();
        var stationsService = scope.ServiceProvider.GetRequiredService<StationsService>();
        var controller = new Controllers.ProfilesController(profilesService, stationsService);

        var profile = await profilesService.CreateAsync(new Profile
        {
            AccountId = Guid.NewGuid().ToString(), Name = "Subscriber"
        });
        var station = await stationsService.CreateAsync(new Station
        {
            AccountId = Guid.NewGuid().ToString(), Name = "Public Station", IsPublic = true
        });

        await controller.Subscribe(profile!.Id, station!.Id);
        await controller.Unsubscribe(profile.Id, station.Id);
        var afterFirst = await profilesService.GetAsync(profile.Id);
        await controller.Unsubscribe(profile.Id, station.Id);
        var afterSecond = await profilesService.GetAsync(profile.Id);

        Assert.That(afterFirst!.SubscribedStationIds, Does.Not.Contain(station.Id));
        Assert.That(afterSecond!.SubscribedStationIds, Does.Not.Contain(station.Id));
    }

    [Test]
    public async Task UpdateProfile_PreservesSubscribedStationIds_WhenPayloadOmitsThem()
    {
        using var scope = _app.Services.CreateScope();
        var profilesService = scope.ServiceProvider.GetRequiredService<ProfilesService>();
        var controller = new Controllers.ProfilesController(
            profilesService,
            scope.ServiceProvider.GetRequiredService<StationsService>());

        var profile = await profilesService.CreateAsync(new Profile
        {
            AccountId = Guid.NewGuid().ToString(),
            Name = "Original",
            SubscribedStationIds = new[] { "station-1", "station-2" }
        });

        profile!.Name = "Renamed";
        profile.SubscribedStationIds = Array.Empty<string>();

        var result = await controller.Update(profile);

        Assert.That(result, Is.InstanceOf<Microsoft.AspNetCore.Mvc.OkObjectResult>());
        var updated = await profilesService.GetAsync(profile.Id);
        Assert.That(updated!.SubscribedStationIds, Is.EquivalentTo(new[] { "station-1", "station-2" }));
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
