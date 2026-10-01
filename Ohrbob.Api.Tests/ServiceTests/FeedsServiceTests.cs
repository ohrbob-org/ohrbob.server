using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ohrbob.Api.Services;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Tests.ServiceTests;

public class FeedsServiceTests
{
    private ApiTestApp<Program> _app = null!;
    private FeedsService _service = null!;

    [SetUp]
    public void Setup()
    {
        _app = new ApiTestApp<Program>(configurationOverride: configuration =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                {"MongoDbLogs:ConnectionString", "mongodb://localhost:27017/TestsOhrbobLogs"},
                {"MongoDbLogs:DatabaseName", "TestsOhrbobLogs"},
                {"MongoDbData:ConnectionString", "mongodb://localhost:27017/TestsOhrbobData"},
                {"MongoDbData:DatabaseName", "TestsOhrbobData"}
            });
        }, setupMongoDbContext: true, createDefaultClient: true);
        _service = _app.Services.GetRequiredService<FeedsService>();
    }

    [TearDown]
    public void TearDown()
    {
        _app?.Dispose();
    }

    [Test]
    public async Task CreateAsync_And_GetAllAsync_ShouldWork()
    {
        var feed = new Feed
        {
            Title = "Test Feed",
            Url = "https://example.com/feed.xml",
            Description = "A test podcast feed"
        };

        var created = await _service.CreateAsync(feed);
        Assert.That(created, Is.Not.Null);
        Assert.That(created!.Id, Is.Not.Empty);

        var allFeeds = await _service.GetAllAsync();
        Assert.That(allFeeds, Is.Not.Empty);
        Assert.That(allFeeds.Any(f => f.Id == created.Id), Is.True);
    }

    [Test]
    public async Task UpdateAsync_ShouldUpdateFields()
    {
        var feed = new Feed
        {
            Title = "Original Title",
            Url = "https://example.com/feed.xml"
        };

        var created = await _service.CreateAsync(feed);
        Assert.That(created, Is.Not.Null);

        created!.Title = "Updated Title";
        created.LastUpdated = DateTime.UtcNow;

        var updated = await _service.UpdateAsync(created);
        Assert.That(updated, Is.Not.Null);
        Assert.That(updated!.Title, Is.EqualTo("Updated Title"));

        var fetched = await _service.GetAsync(created.Id);
        Assert.That(fetched, Is.Not.Null);
        Assert.That(fetched!.Title, Is.EqualTo("Updated Title"));
    }
}
