using MongoDB.Driver;
using Ohrbob.Api.Configuration;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Services;

public class EpisodesService : MongoDbService<Episode>
{
    public EpisodesService(MongoDbContext dbContext, ILogger<MongoDbService<Episode>> logger) : base(dbContext, logger)
    {
    }

    public override UpdateDefinition<Episode> GetUpdateDefinition(Episode entryIn)
    {
        return Builders<Episode>.Update
            .Set(e => e.Guid, entryIn.Guid)
            .Set(e => e.FeedUrl, entryIn.FeedUrl)
            .Set(e => e.FeedId, entryIn.FeedId)
            .Set(e => e.Title, entryIn.Title)
            .Set(e => e.Description, entryIn.Description)
            .Set(e => e.AudioUrl, entryIn.AudioUrl)
            .Set(e => e.ImageUrl, entryIn.ImageUrl)
            .Set(e => e.PublishedAt, entryIn.PublishedAt);
    }
    
    public async Task<Episode?> FindByGuidAndFeedIdAsync(string guid, string feedId)
    {
        var filter = Builders<Episode>.Filter.And(
            Builders<Episode>.Filter.Eq(e => e.Guid, guid),
            Builders<Episode>.Filter.Eq(e => e.FeedId, feedId)
        );
        return await Collection.Find(filter).FirstOrDefaultAsync();
    }

    public async Task<long> CountByFeedIdAsync(string feedId)
    {
        var filter = Builders<Episode>.Filter.Eq(e => e.FeedId, feedId);
        return await Collection.CountDocumentsAsync(filter);
    }

    public async Task<List<Episode>> FindByFeedUrlsAsync(IEnumerable<string> feedUrls)
    {
        if (feedUrls == null || !feedUrls.Any()) return new List<Episode>();
        var filter = Builders<Episode>.Filter.In(e => e.FeedUrl, feedUrls);
        return await Collection.Find(filter).ToListAsync();
    }
}
