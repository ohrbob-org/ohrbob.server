using MongoDB.Driver;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Migrations;

/// <summary>
/// 0.0.13 stores account emails lowercased so the indexed email lookup can detect
/// accounts registered under a different identity provider regardless of casing.
/// Idempotent: documents already normalized are left untouched.
/// </summary>
public class Migration_0_0_13_NormalizeAccountEmails : IMigration
{
    public string Version => "0.0.13";

    public async Task ApplyAsync(IMongoDatabase database, CancellationToken cancellationToken = default)
    {
        var accounts = database.GetCollection<Account>(typeof(Account).Name + "Collection");
        var all = await accounts.Find(Builders<Account>.Filter.Empty).ToListAsync(cancellationToken);

        foreach (var account in all)
        {
            var normalized = (account.Email ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized == account.Email) continue;

            await accounts.UpdateOneAsync(
                Builders<Account>.Filter.Eq(a => a.Id, account.Id),
                Builders<Account>.Update.Set(a => a.Email, normalized),
                cancellationToken: cancellationToken);
        }
    }
}
