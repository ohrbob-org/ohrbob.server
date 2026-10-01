using MongoDB.Driver;
using Ohrbob.Player.Configuration;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Player.Services;

public class ListenStateService : MongoDbService<ListenState>
{
    public ListenStateService(MongoDbContext dbContext) : base(dbContext, null!)
    {
    }

    public override UpdateDefinition<ListenState> GetUpdateDefinition(ListenState entryIn)
    {
        return Builders<ListenState>.Update
            .Set(s => s.IsListened, entryIn.IsListened)
            .Set(s => s.PositionSeconds, entryIn.PositionSeconds);
    }

    public async Task<ListenState?> GetStateAsync(string userId, string episodeId)
    {
        var filter = Builders<ListenState>.Filter.And(
            Builders<ListenState>.Filter.Eq(s => s.UserId, userId),
            Builders<ListenState>.Filter.Eq(s => s.EpisodeId, episodeId)
        );
        filter = Builders<ListenState>.Filter.And(filter, Builders<ListenState>.Filter.Eq(x => x.DeletedAt, null));
        return await Collection.Find(filter).FirstOrDefaultAsync();
    }

    public async Task UpsertStateAsync(string userId, string stationId, string episodeId, int positionSeconds, bool isListened)
    {
        var existing = await GetStateAsync(userId, episodeId);
        if (existing != null)
        {
            existing.PositionSeconds = positionSeconds;
            existing.IsListened = isListened;
            await UpdateAsync(existing);
        }
        else
        {
            await CreateAsync(new ListenState
            {
                UserId = userId,
                StationId = stationId,
                EpisodeId = episodeId,
                PositionSeconds = positionSeconds,
                IsListened = isListened
            });
        }
    }
}
