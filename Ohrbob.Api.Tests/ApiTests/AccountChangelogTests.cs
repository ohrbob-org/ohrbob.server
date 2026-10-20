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

namespace Ohrbob.Api.Tests.ApiTests;

public class AccountChangelogTests
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

    [Test]
    public async Task Can_Update_LastSeenChangelog_On_Account()
    {
        // Arrange
        using var scope = _app.Services.CreateScope();
        var accountService = scope.ServiceProvider.GetRequiredService<AccountService>();
        
        var account = new Account 
        { 
            Email = $"test-{Guid.NewGuid()}@example.com",
            Name = "Test User"
        };
        await accountService.CreateAsync(account);

        // Act
        account.LastSeenChangelog = "v1.2.3";
        await accountService.UpdateAsync(account);

        // Assert
        var updatedAccount = await accountService.GetAsync(account.Id);
        Assert.That(updatedAccount, Is.Not.Null);
        Assert.That(updatedAccount.LastSeenChangelog, Is.EqualTo("v1.2.3"));
    }
}
