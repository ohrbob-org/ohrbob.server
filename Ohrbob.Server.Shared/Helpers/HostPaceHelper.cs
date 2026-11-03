using System.Collections.Generic;
using System.Linq;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Server.Shared.Helpers;

/// <summary>
/// Host Pace: when a station is host-paced, listeners outside the owner account may only play
/// episodes the configured host profile has completed. All rules live here so every client and
/// service applies the same policy.
/// </summary>
public static class HostPaceHelper
{
    public static bool AppliesTo(Station station, string listenerProfileId)
    {
        return station.HostPaceEnabled
               && !string.IsNullOrEmpty(station.HostProfileId)
               && station.HostProfileId != listenerProfileId;
    }

    public static bool IsEpisodeAllowed(
        Station station,
        string listenerAccountId,
        Episode episode,
        HashSet<string> hostCompletedEpisodeIds)
    {
        if (!AppliesTo(station, listenerAccountId)) return true;
        return hostCompletedEpisodeIds.Contains(episode.Id);
    }

    public static HashSet<string> GetUnavailableEpisodeIds(
        Station station,
        string listenerAccountId,
        IEnumerable<Episode> episodes,
        HashSet<string> listenerCompletedEpisodeIds,
        HashSet<string> hostCompletedEpisodeIds)
    {
        var episodeList = episodes.ToList();
        var unavailable = PlaybackStrategyHelper.GetListenedAndAgedEpisodeIds(
            station, episodeList, listenerCompletedEpisodeIds);

        if (AppliesTo(station, listenerAccountId))
        {
            foreach (var episode in episodeList)
            {
                if (!hostCompletedEpisodeIds.Contains(episode.Id))
                    unavailable.Add(episode.Id);
            }
        }

        return unavailable;
    }

    public static List<Episode> GetAllowedEpisodes(
        Station station,
        string listenerAccountId,
        IEnumerable<Episode> episodes,
        HashSet<string> hostCompletedEpisodeIds)
    {
        if (!AppliesTo(station, listenerAccountId)) return episodes.ToList();
        return episodes.Where(e => hostCompletedEpisodeIds.Contains(e.Id)).ToList();
    }
}
