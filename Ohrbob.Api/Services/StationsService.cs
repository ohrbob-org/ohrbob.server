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
            .Set(s => s.ShareCode, entryIn.ShareCode)
            .Set(s => s.IsPublic, entryIn.IsPublic)
            .Set(s => s.Feeds, entryIn.Feeds)
            .Set(s => s.Schedule, entryIn.Schedule)
            .Set(s => s.IsAging, entryIn.IsAging)
            .Set(s => s.HostPaceEnabled, entryIn.HostPaceEnabled)
            .Set(s => s.HostProfileId, entryIn.HostProfileId);
    }

    public async Task<List<Station>> GetByAccountIdAsync(string accountId)
    {
        var filter = Builders<Station>.Filter.Eq(s => s.AccountId, accountId);
        filter = filter.ExcludeDeleted();
        return await Collection.Find(filter).ToListAsync();
    }

    public async Task<Station?> FindByShareCodeAsync(string code)
    {
        var filter = Builders<Station>.Filter.Eq(s => s.ShareCode, code);
        filter = filter.ExcludeDeleted();
        return await Collection.Find(filter).FirstOrDefaultAsync();
    }
}