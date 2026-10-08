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

        var ordered = PlaybackStrategyHelper.GetOrderedPlaylist(episodes, station, listenedSet, currentEpisodeId);
        
        // We want the next episode after currentEpisodeId, or the first one if no current
        var unplayedOrdered = ordered.Where(e => !listenedSet.Contains(e.Id) && e.Id != currentEpisodeId).ToList();
        return unplayedOrdered.FirstOrDefault();
    }
}