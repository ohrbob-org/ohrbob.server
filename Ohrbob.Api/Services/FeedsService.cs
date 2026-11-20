using MongoDB.Driver;
using Ohrbob.Api.Configuration;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Server.Shared.Text;
using OhrbobFeed = Ohrbob.Server.Shared.Data.Feed;

namespace Ohrbob.Api.Services;

public class FeedsService : MongoDbService<OhrbobFeed>
{
    private readonly IFeedReader _feedReader;

    public FeedsService(
        MongoDbContext dbContext,
        ILogger<MongoDbService<OhrbobFeed>> logger,
        IFeedReader feedReader) : base(dbContext, logger)
    {
        _feedReader = feedReader;
    }

    public override UpdateDefinition<OhrbobFeed> GetUpdateDefinition(OhrbobFeed entryIn)
    {
        return Builders<OhrbobFeed>.Update
            .Set(f => f.Url, entryIn.Url)
            .Set(f => f.Title, entryIn.Title)
            .Set(f => f.Description, entryIn.Description)
            .Set(f => f.ImageUrl, entryIn.ImageUrl)
            .Set(f => f.Category, entryIn.Category)
            .Set(f => f.Keywords, entryIn.Keywords)
            .Set(f => f.LastUpdated, entryIn.LastUpdated)
            .Set(f => f.UpdateFrequencyRrule, entryIn.UpdateFrequencyRrule)
            .Set(f => f.NextUpdateAt, entryIn.NextUpdateAt);
    }

    public async Task<List<OhrbobFeed>> GetAllAsync()
    {
        var filter = Builders<OhrbobFeed>.Filter.Empty;
        return await Collection.Find(filter).ToListAsync();
    }

    public async Task<OhrbobFeed?> FindByUrlAsync(string url)
    {
        var filter = Builders<OhrbobFeed>.Filter.Eq(f => f.Url, url);
        return await Collection.Find(filter).FirstOrDefaultAsync();
    }

    public async Task<OhrbobFeed?> ValidateAndParseFeedAsync(string url)
    {
        try
        {
            var parsed = await _feedReader.ReadAsync(url);
            if (parsed == null) return null;

            return new OhrbobFeed
            {
                Url = url,
                Title = HtmlText.ToPlainText(parsed.Title ?? string.Empty),
                Description = parsed.Description ?? string.Empty,
                ImageUrl = parsed.ImageUrl ?? string.Empty,
                Category = FeedMetadataParser.ParseCategory(parsed.Xml),
                Keywords = FeedMetadataParser.ParseKeywords(parsed.Xml) ?? Array.Empty<string>(),
                UpdateFrequencyRrule = FeedMetadataParser.ParseUpdateFrequencyRrule(parsed.Xml)
            };
        }
        catch
        {
            // If it fails to parse, it's not a valid RSS/Atom feed
            return null;
        }
    }
}
