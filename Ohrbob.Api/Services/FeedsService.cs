using MongoDB.Driver;
using Ohrbob.Api.Configuration;
using Ohrbob.Server.Shared.Data;
using CodeHollow.FeedReader;
using Feed = Ohrbob.Server.Shared.Data.Feed;

namespace Ohrbob.Api.Services;

public class FeedsService : MongoDbService<Feed>
{
    public FeedsService(MongoDbContext dbContext, ILogger<MongoDbService<Feed>> logger) : base(dbContext, logger)
    {
    }

    public override UpdateDefinition<Feed> GetUpdateDefinition(Feed entryIn)
    {
        return Builders<Feed>.Update
            .Set(f => f.Url, entryIn.Url)
            .Set(f => f.Title, entryIn.Title)
            .Set(f => f.Description, entryIn.Description)
            .Set(f => f.ImageUrl, entryIn.ImageUrl)
            .Set(f => f.LastUpdated, entryIn.LastUpdated);
    }

    public async Task<List<Feed>> GetAllAsync()
    {
        var filter = Builders<Feed>.Filter.Empty;
        return await Collection.Find(filter).ToListAsync();
    }

    public async Task<Feed?> FindByUrlAsync(string url)
    {
        var filter = Builders<Feed>.Filter.Eq(f => f.Url, url);
        return await Collection.Find(filter).FirstOrDefaultAsync();
    }

    public async Task<Feed?> ValidateAndParseFeedAsync(string url)
    {
        try
        {
            var parsedFeed = await FeedReader.ReadAsync(url);
            if (parsedFeed == null) return null;

            return new Feed
            {
                Url = url,
                Title = parsedFeed.Title ?? string.Empty,
                Description = parsedFeed.Description ?? string.Empty,
                ImageUrl = parsedFeed.ImageUrl ?? string.Empty,
                LastUpdated = DateTime.UtcNow
            };
        }
        catch
        {
            // If it fails to parse, it's not a valid RSS/Atom feed
            return null;
        }
    }
}
