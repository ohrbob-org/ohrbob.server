using MongoDB.Driver;
using Ohrbob.Api.Configuration;
using Ohrbob.Api.Extensions.MongoDb;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Services;

public class StationsService : MongoDbService<Station>
{
    public StationsService(MongoDbContext dbContext, ILogger<MongoDbService<Station>> logger) : base(dbContext, logger)
    {
    }

    public override UpdateDefinition<Station> GetUpdateDefinition(Station entryIn)
    {
        return Builders<Station>.Update
            .Set(s => s.Name, entryIn.Name)
            .Set(s => s.IsPublic, entryIn.IsPublic)
            .Set(s => s.Feeds, entryIn.Feeds)
            .Set(s => s.Schedule, entryIn.Schedule);
    }

    public async Task<List<Station>> GetByAccountIdAsync(string accountId)
    {
        var filter = Builders<Station>.Filter.Eq(s => s.AccountId, accountId);
        filter = filter.ExcludeDeleted();
        return await Collection.Find(filter).ToListAsync();
    }
}