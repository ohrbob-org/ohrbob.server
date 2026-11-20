using Ohrbob.Server.Shared.Text;
using Feed = Ohrbob.Server.Shared.Data.Feed;

namespace Ohrbob.Api.Services;

/// <summary>Result of refreshing one feed.</summary>
public sealed record FeedRefreshOutcome(bool Success, string? Error, long EpisodeCount);

/// <summary>
/// Fetches one feed, stores its metadata and episodes, and deduces when it should be
/// polled again (see <see cref="FeedRefreshScheduler"/>). Shared by the background
/// <see cref="FeedUpdateWorker"/> and the manual "refresh" endpoint.
/// </summary>
public class FeedUpdateService
{
    /// <summary>How long to wait before retrying a feed that could not be read.</summary>
    public static readonly TimeSpan RetryDelayAfterFailure = TimeSpan.FromMinutes(15);

    private readonly IFeedReader _reader;
    private readonly FeedsService _feedsService;
    private readonly EpisodesService _episodesService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FeedUpdateService> _logger;

    public FeedUpdateService(
        IFeedReader reader,
        FeedsService feedsService,
        EpisodesService episodesService,
        TimeProvider timeProvider,
        ILogger<FeedUpdateService> logger)
    {
        _reader = reader;
        _feedsService = feedsService;
        _episodesService = episodesService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// True when the feed's deduced schedule says it is worth fetching now. Feeds that
    /// predate scheduling fall back to the historic hourly cadence.
    /// </summary>
    public static bool IsDue(Feed feed, DateTime now)
    {
        if (feed.NextUpdateAt.HasValue)
            return now >= feed.NextUpdateAt.Value;

        return !feed.LastUpdated.HasValue || feed.LastUpdated.Value <= now.AddHours(-1);
    }

    /// <summary>
    /// Refreshes the feed with the given id. Returns <c>null</c> when no such feed exists.
    /// Failures are reported in <see cref="FeedRefreshOutcome"/> and schedule a retry.
    /// </summary>
    public async Task<FeedRefreshOutcome?> RefreshAsync(string feedId, CancellationToken cancellationToken = default)
    {
        var feed = await _feedsService.GetAsync(feedId);
        if (feed == null) return null;

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        try
        {
            var parsed = await _reader.ReadAsync(feed.Url, cancellationToken);

            feed.Title = HtmlText.ToPlainText(parsed.Title ?? feed.Title);
            feed.Description = parsed.Description ?? feed.Description;
            feed.ImageUrl = parsed.ImageUrl ?? feed.ImageUrl;
            feed.Category = FeedMetadataParser.ParseCategory(parsed.Xml);
            feed.Keywords = FeedMetadataParser.ParseKeywords(parsed.Xml) ?? feed.Keywords;
            feed.UpdateFrequencyRrule = FeedMetadataParser.ParseUpdateFrequencyRrule(parsed.Xml);
            feed.LastUpdated = now;

            // Append-only episode sync: historic episodes are retained, new items are added,
            // re-seen items get refreshed metadata.
            await FeedEpisodeSyncService.SyncAsync(_episodesService, feed, parsed.Items);

            var episodes = await _episodesService.FindByFeedIdAsync(feed.Id);
            var plan = FeedRefreshScheduler.Compute(
                now,
                feed.UpdateFrequencyRrule,
                feed.LastUpdated,
                episodes.Select(e => e.PublishedAt));

            // A feed that has never produced an episode has no pattern to schedule from;
            // check back soon instead of making the user wait an hour for the first sync.
            feed.NextUpdateAt = episodes.Count == 0 ? now.AddMinutes(1) : plan.NextUpdateAt;

            await _feedsService.UpdateAsync(feed);

            _logger.LogInformation(
                "Updated feed {Url}: {EpisodeCount} episodes, next poll {NextUpdateAt:u} ({Cadence}{Rrule})",
                feed.Url, episodes.Count, plan.NextUpdateAt, plan.Cadence,
                plan.FromRrule ? ", from rrule" : string.Empty);

            return new FeedRefreshOutcome(true, null, episodes.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update feed: {Url}", feed.Url);

            feed.NextUpdateAt = now.Add(RetryDelayAfterFailure);
            await _feedsService.UpdateAsync(feed);

            var episodeCount = await _episodesService.CountByFeedIdAsync(feed.Id);
            return new FeedRefreshOutcome(false, ex.Message, episodeCount);
        }
    }
}
