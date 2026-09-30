using MongoDB.Driver;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Extensions.MongoDb;

public static class FilterExtensions
{
    
    public static FilterDefinition<T> ExcludeDeleted<T>(this FilterDefinition<T> filterDefinition) where T : DbEntry
    {
        return Builders<T>.Filter.And(
            filterDefinition,
            Builders<T>.Filter.Eq(entry => entry!.DeletedAt, null)
        );
    }
}