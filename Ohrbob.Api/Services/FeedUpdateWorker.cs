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

                // Update Episodes
                foreach (var item in parsedFeed.Items)
                {
                    var guid = item.Id ?? item.Link ?? item.Title;
                    if (string.IsNullOrEmpty(guid)) continue;

                    var existingEpisode = await episodesService.FindByGuidAndFeedIdAsync(guid, feed.Id);
                    if (existingEpisode == null)
                    {
                        string episodeImageUrl = feed.ImageUrl; // Fallback to feed image
                        string audioUrl = item.Link ?? string.Empty;

                        if (item.SpecificItem?.Element != null)
                        {
                            System.Xml.Linq.XNamespace itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
                            var image = item.SpecificItem.Element.Element(itunes + "image");
                            var href = image?.Attribute("href")?.Value;
                            if (!string.IsNullOrWhiteSpace(href))
                            {
                                episodeImageUrl = href;
                            }

                            // Extract audio enclosure URL
                            var enclosure = item.SpecificItem.Element.Element("enclosure");
                            var enclosureUrl = enclosure?.Attribute("url")?.Value;
                            if (!string.IsNullOrWhiteSpace(enclosureUrl))
                            {
                                audioUrl = enclosureUrl;
                            }
                        }

                        var newEpisode = new Episode
                        {
                            Guid = guid,
                            FeedId = feed.Id,
                            FeedUrl = feed.Url,
                            Title = item.Title ?? string.Empty,
                            Description = item.Description ?? string.Empty,
                            ImageUrl = episodeImageUrl,
                            PublishedAt = item.PublishingDate,
                            AudioUrl = audioUrl
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
