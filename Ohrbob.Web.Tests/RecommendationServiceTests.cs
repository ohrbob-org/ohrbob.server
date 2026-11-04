using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Configuration;
using Ohrbob.Web.Models;
using Ohrbob.Web.Services;

namespace Ohrbob.Web.Tests;

public class RecommendationServiceTests
{
    private Mock<FeedsApiService> _feedsMock = null!;
    private Mock<PodcastIndexApiService> _podcastIndexMock = null!;
    private RecommendationService _service = null!;

    [SetUp]
    public void Setup()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Discover:LaneCategories"] = "Comedy,News"
            })
            .Build();

        var apiLogger = new Mock<ILogger<OhrbobApiService>>();
        var apiOptions = Options.Create(new OhrbobApiOptions { BaseUrl = "https://test.api/", ApiKey = "key" });
        var clientFactory = new Mock<IHttpClientFactory>();

        _feedsMock = new Mock<FeedsApiService>(apiLogger.Object, apiOptions, clientFactory.Object);
        _podcastIndexMock = new Mock<PodcastIndexApiService>(new HttpClient(), config, new Mock<ILogger<PodcastIndexApiService>>().Object);
        _podcastIndexMock.Setup(p => p.GetCategoriesAsync()).ReturnsAsync(new List<string> { "Comedy", "News" });

        _service = new RecommendationService(
            _feedsMock.Object,
            _podcastIndexMock.Object,
            config,
            new Mock<ILogger<RecommendationService>>().Object);
    }

    [Test]
    public async Task BuildLanes_PrefersLocalFeeds_AndCapsAtTwenty()
    {
        var localComedy = Enumerable.Range(1, 20)
            .Select(i => new Feed { Title = $"Local Comedy {i}", Url = $"https://local.test/comedy-{i}", Category = "Comedy" })
            .ToList();
        var localNews = new Feed { Title = "Local News", Url = "https://local.test/news", Category = "News" };

        _feedsMock.Setup(f => f.GetFeedsAsync()).ReturnsAsync(localComedy.Append(localNews).ToList());
        _podcastIndexMock
            .Setup(p => p.GetTrendingAsync("Comedy", RecommendationService.LaneCapacity))
            .ReturnsAsync(new List<PodcastSearchResult>
            {
                new() { Title = "Trending Comedy", Url = "https://trending.test/comedy" },
                new() { Title = "Duplicate Local", Url = "https://local.test/comedy-1" }
            });
        _podcastIndexMock
            .Setup(p => p.GetTrendingAsync("News", RecommendationService.LaneCapacity))
            .ReturnsAsync(new List<PodcastSearchResult> { new() { Title = "Trending News", Url = "https://trending.test/news" } });

        await _service.WarmUpAsync();
        var lanes = await _service.BuildLanesAsync();

        var comedy = lanes.Single(l => l.Category == "Comedy");
        Assert.That(comedy.Podcasts, Has.Count.EqualTo(20));
        Assert.That(comedy.Podcasts.All(p => p.Url.StartsWith("https://local.test/comedy-")), Is.True);

        var news = lanes.Single(l => l.Category == "News");
        Assert.That(news.Podcasts.Select(p => p.Url), Is.EqualTo(new[] { "https://local.test/news", "https://trending.test/news" }));
    }

    [Test]
    public async Task BuildLanes_FallsBackToTrending_WhenNoLocalFeeds()
    {
        _feedsMock.Setup(f => f.GetFeedsAsync()).ReturnsAsync(new List<Feed>());
        _podcastIndexMock
            .Setup(p => p.GetTrendingAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync((string category, int _) => new List<PodcastSearchResult>
            {
                new() { Title = $"{category} one", Url = $"https://trending.test/{category}-1" },
                new() { Title = $"{category} two", Url = $"https://trending.test/{category}-2" }
            });

        await _service.WarmUpAsync();
        var lanes = await _service.BuildLanesAsync();

        Assert.That(lanes.Select(l => l.Category), Is.EqualTo(new[] { "Comedy", "News" }));
        Assert.That(lanes.All(l => l.Podcasts.Count == 2), Is.True);
    }
}
