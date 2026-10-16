using MongoDB.Driver;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Migrations;

/// <summary>
/// 0.0.8 introduced the ApiKeys feature and grew the hot query paths over the
/// 0.0.7 cycle. This migration adds the indexes those paths need. Index creation
/// is idempotent, so re-running is safe.
/// </summary>
public class Migration_0_0_8_AddIndexes : IMigration
{
    public string Version => "0.0.8";

    public async Task ApplyAsync(IMongoDatabase database, CancellationToken cancellationToken = default)
    {
        // Fix duplicate StreamTokens before creating the unique index.
        var profileCollection = database.GetCollection<Profile>("ProfileCollection");
        var profiles = await profileCollection.Find(Builders<Profile>.Filter.Empty).ToListAsync(cancellationToken);
        var seenTokens = new HashSet<string>();
        foreach (var profile in profiles)
        {
            if (string.IsNullOrEmpty(profile.StreamToken) || !seenTokens.Add(profile.StreamToken))
            {
                profile.StreamToken = Guid.NewGuid().ToString();
                await profileCollection.ReplaceOneAsync(
                    Builders<Profile>.Filter.Eq(x => x.Id, profile.Id),
                    profile,
                    cancellationToken: cancellationToken);
                seenTokens.Add(profile.StreamToken);
            }
        }

        // Player resolves an API key by token on every stream request.
        await CreateAsync<ApiKey>(database, cancellationToken,
            Index<ApiKey>("token_unique", Builders<ApiKey>.IndexKeys.Ascending(x => x.Token), unique: true),
            Index<ApiKey>("accountId", Builders<ApiKey>.IndexKeys.Ascending(x => x.AccountId)));

        // Player resolves a profile by its stream token on every stream request.
        await CreateAsync<Profile>(database, cancellationToken,
            Index<Profile>("streamToken_unique", Builders<Profile>.IndexKeys.Ascending(x => x.StreamToken), unique: true),
            Index<Profile>("accountId", Builders<Profile>.IndexKeys.Ascending(x => x.AccountId)));

        // Feed sync looks up an episode by (feed, guid) for every item on every run.
        await CreateAsync<Episode>(database, cancellationToken,
            Index<Episode>("feedId_guid", Builders<Episode>.IndexKeys.Ascending(x => x.FeedId).Ascending(x => x.Guid)),
            Index<Episode>("feedUrl", Builders<Episode>.IndexKeys.Ascending(x => x.FeedUrl)));

        await CreateAsync<Feed>(database, cancellationToken,
            Index<Feed>("url", Builders<Feed>.IndexKeys.Ascending(x => x.Url)));

        await CreateAsync<Account>(database, cancellationToken,
            Index<Account>("email", Builders<Account>.IndexKeys.Ascending(x => x.Email)),
            Index<Account>("identityProvider", Builders<Account>.IndexKeys
                .Ascending("IdentityProviders.Issuer")
                .Ascending("IdentityProviders.UserId")));

        await CreateAsync<Station>(database, cancellationToken,
            Index<Station>("accountId", Builders<Station>.IndexKeys.Ascending(x => x.AccountId)));

        await CreateAsync<ListenState>(database, cancellationToken,
            Index<ListenState>("userId_episodeId", Builders<ListenState>.IndexKeys.Ascending(x => x.UserId).Ascending(x => x.EpisodeId)),
            Index<ListenState>("userId_createdAt", Builders<ListenState>.IndexKeys.Ascending(x => x.UserId).Ascending(x => x.CreatedAt)));
    }

    private static async Task CreateAsync<T>(IMongoDatabase database, CancellationToken cancellationToken, params CreateIndexModel<T>[] indexes)
    {
        var collection = database.GetCollection<T>(typeof(T).Name + "Collection");
        await collection.Indexes.CreateManyAsync(indexes, cancellationToken);
    }

    private static CreateIndexModel<T> Index<T>(string name, IndexKeysDefinition<T> keys, bool unique = false) =>
        new(keys, new CreateIndexOptions { Name = name, Unique = unique });
}
