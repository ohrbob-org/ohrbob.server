using System;
using System.Collections.Generic;
using System.Linq;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Server.Shared.Helpers;

public static class PlaybackStrategyHelper
{
    public static List<Episode> GetOrderedPlaylist(
        IEnumerable<Episode> episodes, 
        Station station, 
        HashSet<string> listenedEpisodeIds, 
        string? activeEpisodeId)
    {
        var unplayed = episodes.Where(e => !listenedEpisodeIds.Contains(e.Id)).ToList();
        
        // Ensure active episode is included even if listened (to show current playhead)
        if (!string.IsNullOrEmpty(activeEpisodeId) && listenedEpisodeIds.Contains(activeEpisodeId))
        {
            var activeEpi = episodes.FirstOrDefault(e => e.Id == activeEpisodeId);
            if (activeEpi != null && !unplayed.Any(u => u.Id == activeEpi.Id))
            {
                unplayed.Add(activeEpi);
            }
        }

        var feedUrls = station.Schedule?.Feeds?.Select(f => f.Url).ToList() ?? station.Feeds?.ToList() ?? new List<string>();

        var result = new List<Episode>();
        var feedQueues = new List<Queue<Episode>>();

        foreach (var feedUrl in feedUrls)
        {
            var feedSchedule = station.Schedule?.Feeds?.FirstOrDefault(f => f.Url == feedUrl);
            var sortOrder = feedSchedule?.SortOrder ?? FeedSortOrder.OldestToNewest;

            var feedEpisodes = unplayed.Where(e => e.FeedUrl == feedUrl);
            
            if (sortOrder == FeedSortOrder.NewestToOldest)
            {
                feedEpisodes = feedEpisodes.OrderByDescending(e => e.PublishedAt);
            }
            else if (sortOrder == FeedSortOrder.Randomized)
            {
                var seed = station.Id?.GetHashCode() ?? 0;
                var random = new Random(seed);
                feedEpisodes = feedEpisodes.OrderBy(e => random.Next());
            }
            else
            {
                feedEpisodes = feedEpisodes.OrderBy(e => e.PublishedAt);
            }
            
            feedQueues.Add(new Queue<Episode>(feedEpisodes));
        }

        bool added;
        do
        {
            added = false;
            foreach (var q in feedQueues)
            {
                if (q.Count > 0)
                {
                    result.Add(q.Dequeue());
                    added = true;
                }
            }
        } while (added);

        return result;
    }
}
