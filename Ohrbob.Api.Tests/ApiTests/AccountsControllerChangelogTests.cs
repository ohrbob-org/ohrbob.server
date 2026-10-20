using NUnit.Framework;
using Ohrbob.Server.Shared.Data;
using Microsoft.Extensions.DependencyInjection;
using Ohrbob.Api.Services;
using MongoDB.Driver;
using System.Threading.Tasks;
using System;
using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Text.Json;
using Ohrbob.Api.Configuration;
using Ohrbob.Server.Shared.Requests;

namespace Ohrbob.Api.Tests.ApiTests;

public class AccountsControllerChangelogTests
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
        }, setupMongoDbContext: true);
        
        _client = _app.CreateDefaultClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _app?.Dispose();
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
    public async Task Can_Update_LastSeenChangelog_Via_Api()
    {
        // Arrange
        var account = await CreateTestAccountAsync();
        var updateRequest = new UpdateLastSeenChangelogRequest { LastSeenChangelog = "v2.0.0" };
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/account/id/{account.Id}/changelog");
        request.Headers.Add("X-API-KEY", _testApiKey);
        request.Content = JsonContent.Create(updateRequest);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.That(response.IsSuccessStatusCode);
        var updatedAccount = await response.Content.ReadFromJsonAsync<Account>();
        Assert.That(updatedAccount, Is.Not.Null);
        Assert.That(updatedAccount.LastSeenChangelog, Is.EqualTo("v2.0.0"));
    }
}
