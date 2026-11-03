using MongoDB.Driver;
using Ohrbob.Api.Configuration;
using Ohrbob.Api.Extensions.Logger;
using Ohrbob.Api.Extensions.MongoDb;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Services;

public abstract class MongoDbService<T> where T : DbEntry
{
    protected readonly IMongoCollection<T> Collection;
    public ILogger<MongoDbService<T>> Logger { get; set; }
    protected string CollectionName { get; private set; }

    public MongoDbService(MongoDbContext dbContext, ILogger<MongoDbService<T>> logger)
    {
        Logger = logger;
        CollectionName = typeof(T).Name + "Collection";
        Collection = dbContext.GetCollection<T>(CollectionName);
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
    
    public async Task<T?> UpdateAsync(T entry)
    {
        var filter = Builders<T>.Filter.Eq(x => x.Id, entry.Id);
        var result = await Collection.UpdateOneAsync(filter, GetUpdateDefinition(entry));
        Logger.LogAudit("Update", entry, result);
        if (!result.IsAcknowledged || result.MatchedCount == 0)
            return null;
        return await GetAsync(entry.Id);
    }
    
    public async Task<T?> GetAsync(string id, bool excludeDeleted = true)
    {
        var filter = Builders<T>.Filter.Eq(x => x.Id, id);
        if (excludeDeleted)
            filter = filter.ExcludeDeleted();
        var result = await Collection.FindAsync(filter);
        return await result.FirstOrDefaultAsync();
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

    public abstract UpdateDefinition<T> GetUpdateDefinition(T entryIn);
}