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
    
    public override UpdateDefinition<Account> GetUpdateDefinition(Account entryIn)
    {
        return Builders<Account>.Update
            .Set(nameof(Account.Email), entryIn.Email)
            .Set(nameof(Account.Name), entryIn.Name)
            .Set(nameof(Account.ProfilePicture), entryIn.ProfilePicture)
            .Set(nameof(Account.IdentityProviders), entryIn.IdentityProviders)
            .Set(nameof(Account.LastUsedProfileId), entryIn.LastUsedProfileId)
            .Set(nameof(Account.NeedsOnboarding), entryIn.NeedsOnboarding)
            .Set(nameof(Account.Tier), entryIn.Tier)
            ;
    }

    public async Task<Account?> FindByEmailAddress(string email)
    {
        var filter = Builders<Account>.Filter.Eq(x => x.Email, email);
        filter.ExcludeDeleted();
        var e = await Collection.FindAsync(filter);
        var result = await e.FirstOrDefaultAsync();
        return result;
    }

    public async Task<Account?> FindByIdpAsync(IdpUser requestIdentityProvider)
    {
        var filter = Builders<Account>.Filter.ElemMatch(x => x.IdentityProviders, idp => idp.Issuer == requestIdentityProvider.Issuer && idp.UserId == requestIdentityProvider.UserId);
        filter.ExcludeDeleted();
        var e = await Collection.FindAsync(filter);
        var result = await e.FirstOrDefaultAsync();
        return result;
    }
}