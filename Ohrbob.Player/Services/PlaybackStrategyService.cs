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

    public async Task<Episode?> GetNextEpisodeAsync(Station station, string? currentEpisodeId, string profileId)
    {
        if (station.Feeds == null || station.Feeds.Length == 0) return null;
        
        var filter = Builders<Episode>.Filter.In(e => e.FeedUrl, station.Feeds);
        var episodes = await _episodes.Find(filter).ToListAsync();

        var listenedEpisodeIds = await _listenStateService.GetListenedEpisodeIdsAsync(profileId);
        var listenedSet = listenedEpisodeIds.ToHashSet();
        listenedSet = PlaybackStrategyHelper.GetListenedAndAgedEpisodeIds(station, episodes, listenedSet);
        // Never return the current episode as "next".
        if (!string.IsNullOrEmpty(currentEpisodeId)) listenedSet.Add(currentEpisodeId);

        // Full deterministic playlist order (including listened) so navigation is positional.
        var ordered = PlaybackStrategyHelper.GetOrderedPlaylist(episodes, station, new HashSet<string>(), currentEpisodeId);

        int startIndex = 0;
        if (!string.IsNullOrEmpty(currentEpisodeId))
        {
            var currentIndex = ordered.FindIndex(e => e.Id == currentEpisodeId);
            startIndex = currentIndex >= 0 ? currentIndex + 1 : 0;
        }

        // Next unplayed episode after the current position.
        var forward = ordered.Skip(startIndex).FirstOrDefault(e => !listenedSet.Contains(e.Id));
        if (forward != null) return forward;

        // Wrap around to the first unplayed episode (end-of-playlist behavior).
        return ordered.FirstOrDefault(e => !listenedSet.Contains(e.Id));
    }

    public async Task<Episode?> GetPreviousEpisodeAsync(Station station, string? currentEpisodeId, string profileId)
    {
        if (station.Feeds == null || station.Feeds.Length == 0) return null;
        if (string.IsNullOrEmpty(currentEpisodeId)) return null;

        var filter = Builders<Episode>.Filter.In(e => e.FeedUrl, station.Feeds);
        var episodes = await _episodes.Find(filter).ToListAsync();

        // Full ordered playlist including listened episodes, so history is navigable.
        var ordered = PlaybackStrategyHelper.GetOrderedPlaylist(episodes, station, new HashSet<string>(), currentEpisodeId);

        var index = ordered.FindIndex(e => e.Id == currentEpisodeId);
        if (index <= 0) return null;
        return ordered[index - 1];
    }
}