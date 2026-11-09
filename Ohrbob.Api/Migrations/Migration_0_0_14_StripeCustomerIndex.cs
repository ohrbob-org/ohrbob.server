using MongoDB.Driver;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Migrations;

/// <summary>
/// 0.0.14 adds the lookup used by the Stripe webhook to find an account by customer.
/// Index creation is idempotent.
/// </summary>
public class Migration_0_0_14_StripeCustomerIndex : IMigration
{
    public string Version => "0.0.14";

    public async Task ApplyAsync(IMongoDatabase database, CancellationToken cancellationToken = default)
    {
        var accounts = database.GetCollection<Account>(typeof(Account).Name + "Collection");
        await accounts.Indexes.CreateOneAsync(
            new CreateIndexModel<Account>(
                Builders<Account>.IndexKeys.Ascending(a => a.StripeCustomerId),
                new CreateIndexOptions { Name = "stripeCustomerId" }),
            cancellationToken: cancellationToken);
    }
}
