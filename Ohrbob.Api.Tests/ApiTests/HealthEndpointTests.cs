using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ohrbob.Api.Configuration;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Tests.ApiTests;

public class HealthEndpointTests
{
    private ApiTestApp<Program> _app;
    private HttpClient _client;

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
    public async Task Health_ReturnsVersionAndTag()
    {
        var response = await _client.GetAsync("/health");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.That(json.RootElement.GetProperty("version").GetString(), Is.Not.Null);
        Assert.That(json.RootElement.GetProperty("tag").GetString(), Is.EqualTo("beta"));
    }
}
