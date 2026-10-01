using CodeHollow.FeedReader;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Services;

public class FeedUpdateWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FeedUpdateWorker> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1);

    public FeedUpdateWorker(IServiceProvider serviceProvider, ILogger<FeedUpdateWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FeedUpdateWorker is starting.");

        // Wait a few seconds before the first run. This gives the host time to fully start
        // and prevents the worker from querying the database during build-time OpenAPI generation.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
        catch (TaskCanceledException)
        {
            return; // Host shut down before the delay finished
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessFeedsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred executing FeedUpdateWorker.");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("FeedUpdateWorker is stopping.");
    }

    private async Task ProcessFeedsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var feedsService = scope.ServiceProvider.GetRequiredService<FeedsService>();
        var episodesService = scope.ServiceProvider.GetRequiredService<EpisodesService>();

        var feeds = await feedsService.GetAllAsync();

        foreach (var feed in feeds)
        {
            _logger.LogInformation("Updating feed: {Url}", feed.Url);
            try
            {
                var parsedFeed = await FeedReader.ReadAsync(feed.Url);
                
                // Update Feed Metadata
                feed.Title = parsedFeed.Title ?? feed.Title;
                feed.Description = parsedFeed.Description ?? feed.Description;
                feed.ImageUrl = parsedFeed.ImageUrl ?? feed.ImageUrl;
                feed.LastUpdated = DateTime.UtcNow;
                await feedsService.UpdateAsync(feed);

                // Update Episodes
                foreach (var item in parsedFeed.Items)
                {
                    var guid = item.Id ?? item.Link ?? item.Title;
                    if (string.IsNullOrEmpty(guid)) continue;

                    var existingEpisode = await episodesService.FindByGuidAndFeedIdAsync(guid, feed.Id);
                    if (existingEpisode == null)
                    {
                        var newEpisode = new Episode
                        {
                            Guid = guid,
                            FeedId = feed.Id,
                            FeedUrl = feed.Url,
                            Title = item.Title ?? string.Empty,
                            PublishedAt = item.PublishingDate,
                            AudioUrl = item.Link ?? string.Empty // Can be improved to extract audio enclosure
                        };
                        await episodesService.CreateAsync(newEpisode);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update feed: {Url}", feed.Url);
            }
        }
    }
}
