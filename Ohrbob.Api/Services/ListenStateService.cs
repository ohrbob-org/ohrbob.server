using MongoDB.Driver;
using Ohrbob.Api.Configuration;
using Ohrbob.Server.Shared.Data;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Ohrbob.Api.Services;

public class ListenStateService : MongoDbService<ListenState>
{
    public ListenStateService(MongoDbContext dbContext, ILogger<MongoDbService<ListenState>> logger) : base(dbContext, logger)
    {
    }

    public override UpdateDefinition<ListenState> GetUpdateDefinition(ListenState entryIn)
    {
        return Builders<ListenState>.Update
            .Set(s => s.IsListened, entryIn.IsListened)
            .Set(s => s.PositionSeconds, entryIn.PositionSeconds);
    }

    public async Task<List<ListenState>> GetStatesByUserIdAsync(string userId)
    {
        var filter = Builders<ListenState>.Filter.And(
            Builders<ListenState>.Filter.Eq(s => s.UserId, userId),
            Builders<ListenState>.Filter.Eq(x => x.DeletedAt, null)
        );
        return await Collection.Find(filter).ToListAsync();
    }
}
