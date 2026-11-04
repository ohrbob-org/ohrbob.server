using MongoDB.Driver;
using Ohrbob.Player.Configuration;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Server.Shared.Helpers;
using System.Linq;
using System.Threading.Tasks;

namespace Ohrbob.Player.Services;

public class PlaybackStrategyService
{
    private readonly IMongoCollection<Episode> _episodes;
    private readonly ListenStateService _listenStateService;
    
    public PlaybackStrategyService(MongoDbContext dbContext, ListenStateService listenStateService)
    {
        _episodes = dbContext.GetCollection<Episode>("EpisodeCollection");
        _listenStateService = listenStateService;
    }

    public async Task<Episode?> GetNextEpisodeAsync(Station station, string? currentEpisodeId, Profile listener)
    {
        if (station.Feeds == null || station.Feeds.Length == 0) return null;

        var filter = Builders<Episode>.Filter.In(e => e.FeedUrl, station.Feeds);
        var episodes = await _episodes.Find(filter).ToListAsync();

        var hostCompleted = await _listenStateService.GetHostCompletedEpisodeIdsAsync(station, episodes);
        var allowedEpisodes = HostPaceHelper.GetAllowedEpisodes(
            station, listener.Id, episodes, hostCompleted);

        var listenedEpisodeIds = await _listenStateService.GetListenedEpisodeIdsAsync(listener.Id);
        var listenedSet = HostPaceHelper.GetUnavailableEpisodeIds(
            station, listener.Id, episodes, listenedEpisodeIds.ToHashSet(), hostCompleted);

        // Never return the current episode as "next".
        if (!string.IsNullOrEmpty(currentEpisodeId)) listenedSet.Add(currentEpisodeId);

        // Only allowed episodes enter the positional playlist; otherwise prev/next could
        // navigate into episodes the host has not released yet.
        var ordered = PlaybackStrategyHelper.GetOrderedPlaylist(
            allowedEpisodes, station, new HashSet<string>(), currentEpisodeId);

        int startIndex = 0;
        if (!string.IsNullOrEmpty(currentEpisodeId))
        {
            var currentIndex = ordered.FindIndex(e => e.Id == currentEpisodeId);
            startIndex = currentIndex >= 0 ? currentIndex + 1 : 0;
        }

        // Next unplayed episode after the current position.
        var forward = ordered.Skip(startIndex).FirstOrDefault(e => !listenedSet.Contains(e.Id));
        if (forward != null) return forward;

        // Endless spin: keep walking the full rotation order, wrapping back to the first episode.
        if (station.EndlessSpin)
        {
            return ordered.Skip(startIndex).FirstOrDefault() ?? ordered.FirstOrDefault();
        }

        // Wrap around to the first unplayed episode (end-of-playlist behavior).
        return ordered.FirstOrDefault(e => !listenedSet.Contains(e.Id));
    }

    public async Task<Episode?> GetPreviousEpisodeAsync(Station station, string? currentEpisodeId, Profile listener)
    {
        if (station.Feeds == null || station.Feeds.Length == 0) return null;
        if (string.IsNullOrEmpty(currentEpisodeId)) return null;

        var filter = Builders<Episode>.Filter.In(e => e.FeedUrl, station.Feeds);
        var episodes = await _episodes.Find(filter).ToListAsync();

        var hostCompleted = await _listenStateService.GetHostCompletedEpisodeIdsAsync(station, episodes);
        var allowedEpisodes = HostPaceHelper.GetAllowedEpisodes(
            station, listener.Id, episodes, hostCompleted);

        // Full ordered playlist including listened episodes, so history is navigable.
        var ordered = PlaybackStrategyHelper.GetOrderedPlaylist(
            allowedEpisodes, station, new HashSet<string>(), currentEpisodeId);

        var index = ordered.FindIndex(e => e.Id == currentEpisodeId);
        if (index < 0) return null;
        if (index == 0)
        {
            return station.EndlessSpin && ordered.Count > 1 ? ordered[^1] : null;
        }
        return ordered[index - 1];
    }

    public async Task<bool> IsEpisodePlayableAsync(Station station, Episode episode, Profile listener)
    {
        if (!HostPaceHelper.AppliesTo(station, listener.Id)) return true;

        var hostCompleted = await _listenStateService.GetHostCompletedEpisodeIdsAsync(station, new[] { episode });
        return HostPaceHelper.IsEpisodeAllowed(station, listener.Id, episode, hostCompleted);
    }
}