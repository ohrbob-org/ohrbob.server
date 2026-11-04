using MongoDB.Driver;
using Ohrbob.Api.Configuration;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Services;

public class ApiKeysService : MongoDbService<ApiKey>
{
    public ApiKeysService(MongoDbContext dbContext, ILogger<ApiKeysService> logger) : base(dbContext, logger)
    {
    }

    public async Task<List<ApiKey>> GetByAccountIdAsync(string accountId)
    {
        var filter = Builders<ApiKey>.Filter.And(
            Builders<ApiKey>.Filter.Eq(x => x.AccountId, accountId),
            Builders<ApiKey>.Filter.Eq(x => x.DeletedAt, null)
        );
        return await Collection.Find(filter).ToListAsync();
    }

    public override UpdateDefinition<ApiKey> GetUpdateDefinition(ApiKey entryIn)
    {
        return Builders<ApiKey>.Update
            .Set(x => x.Name, entryIn.Name)
            .Set(x => x.ProfileId, entryIn.ProfileId);
    }

    public async Task DeleteByAccountIdAsync(string accountId) =>
        await Collection.DeleteManyAsync(k => k.AccountId == accountId);
}
