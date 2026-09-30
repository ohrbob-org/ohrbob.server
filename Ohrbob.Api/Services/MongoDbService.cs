using MongoDB.Driver;
using Ohrbob.Api.Configuration;
using Ohrbob.Api.Extensions.Logger;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Services;

public abstract class MongoDbService<T> where T : DbEntry
{
    protected readonly IMongoCollection<T> Collection;
    public ILogger<MongoDbService<T>> Logger { get; set; }
    protected string CollectionName { get; set; }

    public MongoDbService(MongoDbContext dbContext, ILogger<MongoDbService<T>> logger)
    {
        Logger = logger;
        Collection = dbContext.GetCollection<T>(typeof(T).Name + "Collection");
    }

    public async Task<T?> FindByIdAsync(string id)
    {
        var filter = Builders<T>.Filter.Eq(x => x.Id, id);
        var result = await Collection.Find(filter).FirstOrDefaultAsync();
        return result;
    }
    
    public async Task<T?> CreateAsync(T entry)
    {
        entry.Id = Guid.NewGuid().ToString();
        entry.CreatedAt = DateTime.Now;
        await Collection.InsertOneAsync(entry);
        Logger.LogAudit("Create", entry);
        return entry;
    }
    
    public async Task<bool> RestoreAsync(string id)
    {
        var filter = Builders<T>.Filter.Eq(x => x.Id, id);
        var update = Builders<T>.Update.Set(x => x.DeletedAt, null);
        var result = await Collection.UpdateOneAsync(filter, update);
        Logger.LogAudit("Restore", id, result);
        return result.IsAcknowledged && result.ModifiedCount > 0;
    }
    
    public async Task<bool> DeleteAsync(string id, bool hardDelete = false)
    {
        if (hardDelete)
        {
            return await HardDeleteAsnyc(id);    
        }
        
        var filter = Builders<T>.Filter.Eq(x => x.Id, id);
        var update = Builders<T>.Update.Set(x => x.DeletedAt, DateTime.Now);
        var result = await Collection.UpdateOneAsync(filter, update);
        Logger.LogAudit("Delete", id, result);
        return result.IsAcknowledged && result.ModifiedCount > 0;
    }

    private async Task<bool> HardDeleteAsnyc(string id)
    {
        var filter = Builders<T>.Filter.Eq(x => x.Id, id);
        var result = await Collection.DeleteOneAsync(filter);
        Logger.LogAudit("HardDelete", id, result);
        return result.IsAcknowledged && result.DeletedCount > 0;
    }
}