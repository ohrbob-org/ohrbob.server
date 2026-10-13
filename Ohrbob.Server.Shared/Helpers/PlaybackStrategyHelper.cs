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

        var feedUrls = station.Schedule?.Feeds?.Any() == true 
            ? station.Schedule.Feeds.Select(f => f.Url).ToList() 
            : station.Feeds?.ToList() ?? new List<string>();

        var result = new List<Episode>();
        var feedQueues = new List<(Queue<Episode> Queue, int EpisodesToPlay)>();

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
                feedEpisodes = feedEpisodes.OrderBy(e => GetDeterministicHashCode(station.Id + e.Id));
            }
            else
            {
                feedEpisodes = feedEpisodes.OrderBy(e => e.PublishedAt);
            }
            
            var rawEpisodesToPlay = feedSchedule?.EpisodesToPlay ?? 1;
            var episodesToPlay = rawEpisodesToPlay < 1 ? 1 : (rawEpisodesToPlay > 99 ? 99 : rawEpisodesToPlay);
            feedQueues.Add((new Queue<Episode>(feedEpisodes), episodesToPlay));
        }

        bool added;
        do
        {
            added = false;
            foreach (var item in feedQueues)
            {
                for (int i = 0; i < item.EpisodesToPlay; i++)
                {
                    if (item.Queue.Count > 0)
                    {
                        result.Add(item.Queue.Dequeue());
                        added = true;
                    }
                    else
                    {
                        break;
                    }
                }
            }
        } while (added);

        return result;
    }

    private static int GetDeterministicHashCode(string str)
    {
        if (str == null) return 0;
        unchecked
        {
            int hash = 5381;
            foreach (char c in str)
            {
                hash = ((hash << 5) + hash) ^ c;
            }
            // Non-linear integer mixing (Murmur3 fmix32 equivalent) to scatter sequential suffixes
            uint h = (uint)hash;
            h ^= h >> 16;
            h *= 0x85ebca6b;
            h ^= h >> 13;
            h *= 0xc2b2ae35;
            h ^= h >> 16;
            return (int)h;
        }
    }

    public static HashSet<string> GetListenedAndAgedEpisodeIds(
        Station station, 
        IEnumerable<Episode> episodes, 
        HashSet<string> listenedEpisodeIds)
    {
        var result = new HashSet<string>(listenedEpisodeIds);
        if (station != null && station.IsAging && station.Feeds != null)
        {
            var episodesList = episodes.ToList();
            foreach (var feedUrl in station.Feeds)
            {
                var feedEpisodes = episodesList.Where(e => e.FeedUrl == feedUrl).ToList();
                if (feedEpisodes.Any())
                {
                    var newestEpisode = feedEpisodes
                        .OrderByDescending(e => e.PublishedAt ?? DateTime.MinValue)
                        .ThenByDescending(e => e.CreatedAt)
                        .First();
                    
                    foreach (var ep in feedEpisodes)
                    {
                        if (ep.Id != newestEpisode.Id)
                        {
                            result.Add(ep.Id);
                        }
                    }
                }
            }
        }
        return result;
    }
}
