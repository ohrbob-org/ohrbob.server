using MongoDB.Driver;
using Ohrbob.Api.Configuration;
using Ohrbob.Api.Extensions.MongoDb;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Services;

public class AccountService : MongoDbService<Account>
{
    public AccountService(MongoDbContext dbContext, ILogger<AccountService> logger) : base(dbContext, logger)
    {
    }

    public async Task<Account?> FindByEmailAddress(string email)
    {
        var filter = Builders<Account>.Filter.Eq(x => x.Email, email);
        filter.ExcludeDeleted();
        var e = await Collection.FindAsync(filter);
        var result = await e.FirstOrDefaultAsync();
        return result;
    }

}