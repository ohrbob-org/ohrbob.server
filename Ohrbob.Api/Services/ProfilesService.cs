using MongoDB.Driver;
using Ohrbob.Api.Configuration;
using Ohrbob.Api.Extensions.MongoDb;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Services;

public class ProfilesService : MongoDbService<Profile>
{
    public ProfilesService(MongoDbContext dbContext, ILogger<MongoDbService<Profile>> logger) : base(dbContext, logger)
    {
    }

    public override UpdateDefinition<Profile> GetUpdateDefinition(Profile entryIn)
    {
        return Builders<Profile>.Update
            .Set(p => p.Name, entryIn.Name)
            .Set(p => p.StreamToken, entryIn.StreamToken)
            .Set(p => p.AllowedStationIds, entryIn.AllowedStationIds)
            .Set(p => p.SubscribedStationIds, entryIn.SubscribedStationIds)
            .Set(p => p.LastActiveStationId, entryIn.LastActiveStationId)
            .Set(p => p.LastActiveEpisodeId, entryIn.LastActiveEpisodeId);
    }

    public async Task<List<Profile>> GetByAccountIdAsync(string accountId)
    {
        var filter = Builders<Profile>.Filter.Eq(p => p.AccountId, accountId);
        filter = filter.ExcludeDeleted();
        return await Collection.Find(filter).ToListAsync();
    }

    public async Task<List<Profile>> GetBySubscribedStationIdAsync(string stationId)
    {
        var filter = Builders<Profile>.Filter.AnyEq(p => p.SubscribedStationIds, stationId);
        filter = filter.ExcludeDeleted();
        return await Collection.Find(filter).ToListAsync();
    }

    public async Task DeleteByAccountIdAsync(string accountId) =>
        await Collection.DeleteManyAsync(p => p.AccountId == accountId);
}
