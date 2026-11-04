using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Ohrbob.Web.Models;

namespace Ohrbob.Web.Services;

/// <summary>
/// Builds the Discover recommendation lanes: feeds already added to stations first,
/// topped up with PodcastIndex trending feeds. Trending is fetched once and cached.
/// </summary>
public class RecommendationService
{
    public const string DefaultLaneCategories = "Comedy,News,True Crime,Business,Health & Fitness";
    public const int LaneCapacity = 20;

    private readonly FeedsApiService _feedsApi;
    private readonly PodcastIndexApiService _podcastIndex;
    private readonly ILogger<RecommendationService> _logger;
    private readonly ConcurrentDictionary<string, List<PodcastSearchResult>> _trending = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _warmupGate = new(1, 1);
    private readonly SemaphoreSlim _rosterGate = new(1, 1);
    private IReadOnlyList<string> _categoryRoster = Array.Empty<string>();
    private bool _rosterLoaded;
    private bool _warmedUp;

    public string[] Categories { get; }

    public RecommendationService(
        FeedsApiService feedsApi,
        PodcastIndexApiService podcastIndex,
        IConfiguration configuration,
        ILogger<RecommendationService> logger)
    {
        _feedsApi = feedsApi;
        _podcastIndex = podcastIndex;
        _logger = logger;

        var configured = configuration["Discover:LaneCategories"];
        Categories = (string.IsNullOrWhiteSpace(configured) ? DefaultLaneCategories : configured)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>Fetches trending feeds for every lane. Safe to call more than once.</summary>
    public async Task WarmUpAsync(CancellationToken cancellationToken = default)
    {
        if (_warmedUp) return;

        await _warmupGate.WaitAsync(cancellationToken);
        try
        {
            if (_warmedUp) return;

            foreach (var category in Categories)
            {
                try
                {
                    _trending[category] = await _podcastIndex.GetTrendingAsync(category, LaneCapacity);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load trending podcasts for category {Category}.", category);
                    _trending[category] = new List<PodcastSearchResult>();
                }
            }

            await GetCategoryRosterAsync(cancellationToken);
            _warmedUp = true;
        }
        finally
        {
            _warmupGate.Release();
        }
    }

    /// <summary>PodcastIndex category names for the Discover category buttons.</summary>
    public virtual async Task<IReadOnlyList<string>> GetCategoryRosterAsync(CancellationToken cancellationToken = default)
    {
        if (_rosterLoaded) return _categoryRoster;

        await _rosterGate.WaitAsync(cancellationToken);
        try
        {
            if (_rosterLoaded) return _categoryRoster;

            _categoryRoster = await _podcastIndex.GetCategoriesAsync() ?? new List<string>();
            _rosterLoaded = true;
            return _categoryRoster;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load PodcastIndex category roster.");
            return _categoryRoster;
        }
        finally
        {
            _rosterGate.Release();
        }
    }

    public virtual async Task<List<RecommendationLane>> BuildLanesAsync()
    {
        var feeds = await _feedsApi.GetFeedsAsync() ?? new List<Ohrbob.Server.Shared.Data.Feed>();
        var usedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lanes = new List<RecommendationLane>();

        foreach (var category in Categories)
        {
            var lane = new RecommendationLane { Category = category };

            foreach (var feed in feeds.Where(f => string.Equals(f.Category, category, StringComparison.OrdinalIgnoreCase)))
            {
                if (lane.Podcasts.Count >= LaneCapacity) break;
                if (string.IsNullOrWhiteSpace(feed.Url) || !usedUrls.Add(feed.Url)) continue;

                lane.Podcasts.Add(new PodcastSearchResult
                {
                    Title = feed.Title,
                    Url = feed.Url,
                    Image = feed.ImageUrl
                });
            }

            lanes.Add(lane);
        }

        foreach (var lane in lanes)
        {
            if (!_trending.TryGetValue(lane.Category, out var trending)) continue;

            foreach (var podcast in trending)
            {
                if (lane.Podcasts.Count >= LaneCapacity) break;
                if (string.IsNullOrWhiteSpace(podcast.Url) || !usedUrls.Add(podcast.Url)) continue;

                lane.Podcasts.Add(podcast);
            }
        }

        return lanes;
    }
}
