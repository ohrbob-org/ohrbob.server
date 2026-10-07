using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace Ohrbob.Player.Tests;

public class HealthEndpointTests
{
    [Test]
    public async Task Health_ReturnsVersionTagAndMongoStatus_WithMongo()
    {
        using var app = new ApiTestApp<Program>(
            createDefaultClient: false,
            setupMongoDbContext: true,
            databaseName: "TestDb_" + Guid.NewGuid().ToString(),
            configurationOverride: b => b.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppVersion:Version"] = "abc123",
                ["AppVersion:Tag"] = "beta"
            }));
        using var client = app.CreateClient();
        var response = await client.GetAsync("/health");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Multiple(() =>
        {
            Assert.That(json!.RootElement.GetProperty("version").GetString(), Is.EqualTo("abc123"));
            Assert.That(json!.RootElement.GetProperty("tag").GetString(), Is.EqualTo("beta"));
            Assert.That(json!.RootElement.GetProperty("checks").GetProperty("mongodb").GetString(), Is.EqualTo("Healthy"));
        });
    }
}
