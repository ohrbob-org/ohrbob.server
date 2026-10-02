using System;
using System.Collections.Generic;
using System.Linq;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Server.Shared.Helpers;

public static class PlaybackStrategyHelper
{
    public static List<Episode> GetOrderedPlaylist(
        IEnumerable<Episode> episodes, 
        PlaybackStrategy strategy, 
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

        switch (strategy)
        {
            case PlaybackStrategy.Random:
                return unplayed.OrderBy(e => Guid.NewGuid()).ToList();
            case PlaybackStrategy.ChronologicalRoundRobin:
            case PlaybackStrategy.Chronological:
            default:
                return unplayed.OrderBy(e => e.PublishedAt).ToList();
        }
    }
}
