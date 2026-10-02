using MongoDB.Driver;
using Ohrbob.Player.Configuration;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Player.Services;

public class PlaybackStrategyService
{
    private readonly IMongoCollection<Episode> _episodes;
    
    public PlaybackStrategyService(MongoDbContext dbContext)
    {
        _episodes = dbContext.GetCollection<Episode>("EpisodeCollection");
    }

    // A simple mock for now that just grabs a random episode from the station's feeds
    public async Task<Episode?> GetNextEpisodeAsync(Station station, string? currentEpisodeId)
    {
        if (station.Feeds == null || station.Feeds.Length == 0) return null;
        
        var filter = Builders<Episode>.Filter.In(e => e.FeedId, station.Feeds);
        
        if (!string.IsNullOrEmpty(currentEpisodeId))
        {
             filter = Builders<Episode>.Filter.And(filter, Builders<Episode>.Filter.Ne(e => e.Id, currentEpisodeId));
        }

        // Just get the newest one not equal to current for this simplified implementation
        var sort = Builders<Episode>.Sort.Descending(e => e.PublishedAt);
        return await _episodes.Find(filter).Sort(sort).FirstOrDefaultAsync();
    }
}
