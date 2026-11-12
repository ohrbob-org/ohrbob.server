using CodeHollow.FeedReader;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Server.Shared.Text;

namespace Ohrbob.Api.Services;

public class FeedUpdateWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FeedUpdateWorker> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(5);

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

            try 
            {
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
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
            // if a feed has 0 episodes, we try to query it again to see if it has been updated.
            
            var anyEpisodes = await episodesService.CountByFeedIdAsync(feed.Id);
            var lastUpdateIfAny = feed.LastUpdated;
            if (anyEpisodes == 0 && lastUpdateIfAny > DateTime.UtcNow.AddMinutes(-1))
            {
                _logger.LogInformation("Skipping feed update as it was updated recently (within the last minute) and resulted in 0 episodes: {Url}", feed.Url);
                continue;
            }
            
            if (anyEpisodes > 0 && lastUpdateIfAny > DateTime.UtcNow.AddHours(-1))
            {
                _logger.LogInformation("Skipping feed update as it was updated recently (within the last hour): {Url}", feed.Url);
                continue;
            }

            _logger.LogInformation("Updating feed: {Url}", feed.Url);
            try
            {
                var parsedFeed = await FeedReader.ReadAsync(feed.Url);
                
                // Update Feed Metadata
                feed.Title = HtmlText.ToPlainText(parsedFeed.Title ?? feed.Title);
                feed.Description = parsedFeed.Description ?? feed.Description;
                feed.ImageUrl = parsedFeed.ImageUrl ?? feed.ImageUrl;
                feed.Category = FeedMetadataParser.ParseCategory(parsedFeed.OriginalDocument as string);
                feed.LastUpdated = DateTime.UtcNow;

                if (parsedFeed.OriginalDocument is string xmlContent && !string.IsNullOrWhiteSpace(xmlContent))
                {
                    try
                    {
                        var xDoc = System.Xml.Linq.XDocument.Parse(xmlContent);
                        System.Xml.Linq.XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
                        var keywordsElement = xDoc.Descendants(itunes + "keywords").FirstOrDefault();
                        if (keywordsElement != null && !string.IsNullOrWhiteSpace(keywordsElement.Value))
                        {
                            feed.Keywords = keywordsElement.Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                                                 .Select(k => k.Trim())
                                                                 .Where(k => !string.IsNullOrEmpty(k))
                                                                 .ToArray();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse XML for keywords for feed: {Url}", feed.Url);
                    }
                }

                await feedsService.UpdateAsync(feed);

                // Update Episodes (append-only: historic episodes are retained,
                // new items are added, re-seen items get refreshed metadata).
                await FeedEpisodeSyncService.SyncAsync(episodesService, feed, parsedFeed.Items);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update feed: {Url}", feed.Url);
            }
        }
    }
}
