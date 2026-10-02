using MongoDB.Driver;
using Ohrbob.Player.Configuration;
using Ohrbob.Server.Shared.Data;

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
        
        if (!string.IsNullOrEmpty(currentEpisodeId))
        {
             filter = Builders<Episode>.Filter.And(filter, Builders<Episode>.Filter.Ne(e => e.Id, currentEpisodeId));
        }

        // Fetch all listened episode IDs for this profile to exclude them
        var listenedEpisodeIds = await _listenStateService.GetListenedEpisodeIdsAsync(profileId);
        if (listenedEpisodeIds.Any())
        {
            filter = Builders<Episode>.Filter.And(filter, Builders<Episode>.Filter.Nin(e => e.Id, listenedEpisodeIds));
        }

        switch (station.Strategy)
        {
            case PlaybackStrategy.Random:
                // Let's use a simpler aggregation that avoids manually rendering the filter with BsonSerializer overhead
                var randomCursor = await _episodes.Aggregate()
                    .Match(filter)
                    .Sample(1)
                    .ToListAsync();
                return randomCursor.FirstOrDefault();
                
            case PlaybackStrategy.ChronologicalRoundRobin:
                // Simplified implementation: just chronologically for now, true round-robin requires complex state tracking across feeds
                goto case PlaybackStrategy.Chronological;

            case PlaybackStrategy.Chronological:
            default:
                // Oldest unplayed episode
                var sort = Builders<Episode>.Sort.Ascending(e => e.PublishedAt);
                return await _episodes.Find(filter).Sort(sort).FirstOrDefaultAsync();
        }
    }
}
