using CodeHollow.FeedReader;
using OhrbobFeed = Ohrbob.Server.Shared.Data.Feed;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Services;

/// <summary>
/// Synchronizes stored episodes with freshly parsed feed items.
/// Episodes are append-only: items that disappeared from the feed are deliberately
/// retained (historic playlist), new items are added, and metadata of re-seen
/// items is refreshed.
/// </summary>
public static class FeedEpisodeSyncService
{
    public static async Task SyncAsync(EpisodesService episodesService, OhrbobFeed feed, IEnumerable<FeedItem> items)
    {
        foreach (var item in items)
        {
            var guid = item.Id ?? item.Link ?? item.Title;
            if (string.IsNullOrEmpty(guid)) continue;

            var (audioUrl, episodeImageUrl) = ExtractMediaUrls(feed.ImageUrl, item);

            var existingEpisode = await episodesService.FindByGuidAndFeedIdAsync(guid, feed.Id);
            if (existingEpisode == null)
            {
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
            else if (existingEpisode.Title != (item.Title ?? string.Empty)
                || existingEpisode.Description != (item.Description ?? string.Empty)
                || existingEpisode.AudioUrl != audioUrl
                || existingEpisode.ImageUrl != episodeImageUrl
                || existingEpisode.PublishedAt != item.PublishingDate
                || existingEpisode.FeedUrl != feed.Url)
            {
                existingEpisode.Title = item.Title ?? string.Empty;
                existingEpisode.Description = item.Description ?? string.Empty;
                existingEpisode.AudioUrl = audioUrl;
                existingEpisode.ImageUrl = episodeImageUrl;
                existingEpisode.PublishedAt = item.PublishingDate;
                existingEpisode.FeedUrl = feed.Url;
                await episodesService.UpdateAsync(existingEpisode);
            }
        }
    }

    private static (string AudioUrl, string ImageUrl) ExtractMediaUrls(string feedImageUrl, FeedItem item)
    {
        string episodeImageUrl = feedImageUrl; // Fallback to feed image
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

        return (audioUrl, episodeImageUrl);
    }
}
