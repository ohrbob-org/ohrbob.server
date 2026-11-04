using Ohrbob.Api.Services;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Ohrbob.Api.Configuration;
using Ohrbob.Api.Extensions;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Server.Shared.Requests;
using ApiKey = Ohrbob.Server.Shared.Data.ApiKey;

namespace Ohrbob.Api.Tests.ApiTests;

public class AccountsControllerTests
{
    private ApiTestApp<Program> _app;
    private HttpClient _client;

    private static readonly CreateAccountRequest CreateAccountRequest = new CreateAccountRequest()
    {
        Email = "test@example.com",
        Name = "Max Mustermann",
        ProfilePicture = "https://example.com/profile.jpg",
        IdentityProvider = new IdpUser() { Issuer = "https://accounts.example.com", UserId = "1234567890" }
    };
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
            
        }, setupMongoDbContext: true);
        _client = _app.CreateDefaultClient();
        var key = _app.Services.GetRequiredService<IConfiguration>()["ApiKeys:0:Key"];
        
        Assert.That(key, Is.Not.Null);
        
        
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _app?.Dispose();
    }
    
    [Test]
    public async Task CreateAccount_Works()
    {
        var createRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/account/create");
        createRequest.Headers.Add("X-API-KEY", _testApiKey);
        createRequest.Content = JsonContent.Create(CreateAccountRequest); // StringContent("{}", Encoding.UTF8, "application/json");
        var createResponse = await _client.SendAsync(createRequest);
        Assert.That(createResponse.IsSuccessStatusCode);
        Assert.That(createResponse.Headers.Location, Is.Not.Null);
        Assert.That(createResponse.Headers.Location.ToString().ToLowerInvariant().Contains("api/account/"));
        
        var getRequest = new HttpRequestMessage(HttpMethod.Get, createResponse.Headers.Location);
        getRequest.Headers.Add("X-API-KEY", _testApiKey);
        var getResponse = await _client.SendAsync(getRequest);
        Assert.That(getResponse.IsSuccessStatusCode);
        var account = await getResponse.Content.ReadFromJsonAsync<Account>();
        
        Assert.That(account, Is.Not.Null);
        Assert.That(account.Email, Is.EqualTo(CreateAccountRequest.Email));
        Assert.That(account.Name, Is.EqualTo(CreateAccountRequest.Name));
        Assert.That(account.ProfilePicture, Is.EqualTo(CreateAccountRequest.ProfilePicture));
        Assert.That(account.IdentityProviders.Count, Is.EqualTo(1));
        Assert.That(account.IdentityProviders[0].Issuer, Is.EqualTo(CreateAccountRequest.IdentityProvider.Issuer));
        Assert.That(account.IdentityProviders[0].UserId, Is.EqualTo(CreateAccountRequest.IdentityProvider.UserId));
    }

    private async Task<Account> CreateTestAccountAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/account/create");
        request.Headers.Add("X-API-KEY", _testApiKey);
        var createReq = new CreateAccountRequest()
        {
            Email = $"test_{Guid.NewGuid()}@example.com",
            Name = "Jane Doe",
            ProfilePicture = "https://example.com/pic.jpg",
            IdentityProvider = new IdpUser { Issuer = "https://example.com", UserId = Guid.NewGuid().ToString() }
        };
        request.Content = JsonContent.Create(createReq);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        
        var getRequest = new HttpRequestMessage(HttpMethod.Get, response.Headers.Location);
        getRequest.Headers.Add("X-API-KEY", _testApiKey);
        var getResponse = await _client.SendAsync(getRequest);
        return await getResponse.Content.ReadFromJsonAsync<Account>();
    }

    [Test]
    public async Task GetAccountById_Works()
    {
        var account = await CreateTestAccountAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/account/id/{account.Id}");
        request.Headers.Add("X-API-KEY", _testApiKey);
        
        var response = await _client.SendAsync(request);
        Assert.That(response.IsSuccessStatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<Account>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Id, Is.EqualTo(account.Id));
    }

    [Test]
    public async Task GetAccountByEmail_Works()
    {
        var account = await CreateTestAccountAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/account/email/{account.Email}");
        request.Headers.Add("X-API-KEY", _testApiKey);
        
        var response = await _client.SendAsync(request);
        Assert.That(response.IsSuccessStatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<Account>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Email, Is.EqualTo(account.Email));
    }

    [Test]
    public async Task AddIdp_Works()
    {
        var account = await CreateTestAccountAsync();
        var idpRequest = new IdentityProviderRequest
        {
            AccountId = account.Id,
            Issuer = "https://newissuer.com",
            UserId = "newuserid123"
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/account/idp");
        request.Headers.Add("X-API-KEY", _testApiKey);
        request.Content = JsonContent.Create(idpRequest);
        
        var response = await _client.SendAsync(request);
        Assert.That(response.IsSuccessStatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<Account>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result.IdentityProviders.Count, Is.EqualTo(2));
        Assert.That(result.IdentityProviders.Any(x => x.Issuer == "https://newissuer.com"));
    }

    [Test]
    public async Task RemoveIdp_Works()
    {
        var account = await CreateTestAccountAsync();
        var idpRequest = new IdentityProviderRequest
        {
            AccountId = account.Id,
            Issuer = account.IdentityProviders[0].Issuer,
            UserId = account.IdentityProviders[0].UserId
        };

        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/account/idp");
        request.Headers.Add("X-API-KEY", _testApiKey);
        request.Content = JsonContent.Create(idpRequest);
        
        var response = await _client.SendAsync(request);
        Assert.That(response.IsSuccessStatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<Account>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result.IdentityProviders.Count, Is.EqualTo(0));
    }
    [Test]
    public async Task SetActiveProfile_UpdatesLastUsedProfileId()
    {
        var account = await CreateTestAccountAsync();
        var profileService = _app.Services.CreateScope().ServiceProvider.GetRequiredService<ProfilesService>();
        var profile = new Profile { AccountId = account.Id, Name = "Test Profile" };
        await profileService.CreateAsync(profile);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/account/{account.Id}/active-profile/{profile.Id}");
        request.Headers.Add("X-API-KEY", _testApiKey);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var updatedAccount = await response.Content.ReadFromJsonAsync<Account>();
        Assert.That(updatedAccount, Is.Not.Null);
        Assert.That(updatedAccount.LastUsedProfileId, Is.EqualTo(profile.Id));
    }

    [Test]
    public async Task DeleteAccount_RemovesOwnedDataAndStationReferences()
    {
        var account = await CreateTestAccountAsync();
        var otherAccount = await CreateTestAccountAsync();
        var db = _app.MongoDbContext!;

        var profile = new Profile { Id = Guid.NewGuid().ToString(), AccountId = account.Id, Name = "Owner Profile", StreamToken = Guid.NewGuid().ToString() };
        var otherProfile = new Profile
        {
            Id = Guid.NewGuid().ToString(),
            AccountId = otherAccount.Id,
            Name = "Subscriber",
            StreamToken = Guid.NewGuid().ToString(),
            AllowedStationIds = new[] { "station-1" },
            SubscribedStationIds = new[] { "station-1" },
            LastActiveStationId = "station-1",
            LastActiveEpisodeId = "episode-1"
        };
        await db.GetCollection<Profile>("ProfileCollection").InsertManyAsync(new[] { profile, otherProfile });

        await db.GetCollection<Station>("StationCollection").InsertOneAsync(new Station
        {
            Id = "station-1", AccountId = account.Id, Name = "Station", Feeds = Array.Empty<string>()
        });
        await db.GetCollection<ListenState>("ListenStateCollection").InsertManyAsync(new[]
        {
            new ListenState { Id = Guid.NewGuid().ToString(), UserId = profile.Id, StationId = "station-1", EpisodeId = "episode-1" },
            new ListenState { Id = Guid.NewGuid().ToString(), UserId = otherProfile.Id, StationId = "station-1", EpisodeId = "episode-1" }
        });
        await db.GetCollection<ApiKey>("ApiKeyCollection").InsertOneAsync(new ApiKey
        {
            Id = Guid.NewGuid().ToString(), AccountId = account.Id, Name = "Key", Token = "token"
        });

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/account/id/{account.Id}");
        request.Headers.Add("X-API-KEY", _testApiKey);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        Assert.That(await db.GetCollection<Account>("AccountCollection").Find(a => a.Id == account.Id).FirstOrDefaultAsync(), Is.Null);
        Assert.That(await db.GetCollection<Profile>("ProfileCollection").Find(p => p.AccountId == account.Id).FirstOrDefaultAsync(), Is.Null);
        Assert.That(await db.GetCollection<Station>("StationCollection").Find(s => s.AccountId == account.Id).FirstOrDefaultAsync(), Is.Null);
        Assert.That(await db.GetCollection<ListenState>("ListenStateCollection").Find(s => s.StationId == "station-1").FirstOrDefaultAsync(), Is.Null);
        Assert.That(await db.GetCollection<ApiKey>("ApiKeyCollection").Find(k => k.AccountId == account.Id).FirstOrDefaultAsync(), Is.Null);

        var updatedOtherProfile = await db.GetCollection<Profile>("ProfileCollection")
            .Find(p => p.Id == otherProfile.Id).FirstOrDefaultAsync();
        Assert.That(updatedOtherProfile, Is.Not.Null);
        Assert.That(updatedOtherProfile.SubscribedStationIds, Is.Empty);
        Assert.That(updatedOtherProfile.AllowedStationIds, Is.Empty);
        Assert.That(updatedOtherProfile.LastActiveStationId, Is.Null);
        Assert.That(updatedOtherProfile.LastActiveEpisodeId, Is.Null);
    }
}
