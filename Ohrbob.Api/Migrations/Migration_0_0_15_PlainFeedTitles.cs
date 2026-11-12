using MongoDB.Driver;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Server.Shared.Text;

namespace Ohrbob.Api.Migrations;

/// <summary>
/// 0.0.15 stores feed titles as plain text. Feeds parsed before this version kept the raw
/// RSS title, which can contain HTML markup (e.g. <c>&lt;a href="..."&gt;Show&lt;/a&gt;</c>)
/// that the UI renders literally. This migration cleans existing titles so they display
/// correctly without waiting for the next feed update. It is idempotent: plain titles are
/// left untouched.
/// </summary>
public class Migration_0_0_15_PlainFeedTitles : IMigration
{
    public string Version => "0.0.15";

    public async Task ApplyAsync(IMongoDatabase database, CancellationToken cancellationToken = default)
    {
        var feeds = database.GetCollection<Feed>(typeof(Feed).Name + "Collection");
        var all = await feeds.Find(Builders<Feed>.Filter.Empty).ToListAsync(cancellationToken);

        foreach (var feed in all)
        {
            var title = HtmlText.ToPlainText(feed.Title);
            if (title == feed.Title)
                continue;

            var update = Builders<Feed>.Update.Set(f => f.Title, title);
            await feeds.UpdateOneAsync(
                Builders<Feed>.Filter.Eq(f => f.Id, feed.Id),
                update,
                cancellationToken: cancellationToken);
        }
    }
}
