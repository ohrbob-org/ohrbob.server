using MongoDB.Driver;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Migrations;

/// <summary>
/// 0.0.16 gives every existing feed an initial refresh schedule. Feeds created before
/// scheduling existed have no <see cref="Feed.NextUpdateAt"/>, so they would fall back to
/// the hourly poll — which is fine, but the first poll would be up to five seconds away
/// for every feed at once. Seeding "last update + 1 hour" preserves the old cadence and
/// spreads the initial polls. Idempotent: feeds that already have a schedule are skipped.
/// </summary>
public class Migration_0_0_16_FeedRefreshSchedules : IMigration
{
    public string Version => "0.0.16";

    public async Task ApplyAsync(IMongoDatabase database, CancellationToken cancellationToken = default)
    {
        var feeds = database.GetCollection<Feed>(typeof(Feed).Name + "Collection");
        var withoutSchedule = Builders<Feed>.Filter.Eq(f => f.NextUpdateAt, null);
        var pending = await feeds.Find(withoutSchedule).ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var feed in pending)
        {
            var nextUpdateAt = (feed.LastUpdated ?? now).AddHours(1);
            var update = Builders<Feed>.Update.Set(f => f.NextUpdateAt, nextUpdateAt);
            await feeds.UpdateOneAsync(
                Builders<Feed>.Filter.Eq(f => f.Id, feed.Id),
                update,
                cancellationToken: cancellationToken);
        }
    }
}
