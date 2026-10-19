using MongoDB.Driver;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Server.Shared.Text;

namespace Ohrbob.Api.Migrations;

/// <summary>
/// 0.0.9 introduced <see cref="Episode.PlainDescription"/>, a plain-text rendering of the
/// feed-provided HTML description. Episodes stored before this version still carry raw,
/// entity-encoded HTML (e.g. <c>R&amp;#252;cktritt</c>) in <see cref="Episode.Description"/>,
/// which the UI would render literally. This migration backfills the plain-text field from
/// the stored description so existing playlists display correctly without waiting for a
/// re-sync. It is idempotent: the plain text is recomputed from the source description.
/// </summary>
public class Migration_0_0_9_BackfillPlainDescriptions : IMigration
{
    public string Version => "0.0.9";

    public async Task ApplyAsync(IMongoDatabase database, CancellationToken cancellationToken = default)
    {
        var episodes = database.GetCollection<Episode>(typeof(Episode).Name + "Collection");
        var all = await episodes.Find(Builders<Episode>.Filter.Empty).ToListAsync(cancellationToken);

        foreach (var episode in all)
        {
            var plainDescription = HtmlText.ToPlainText(episode.Description);
            if (episode.PlainDescription == plainDescription)
                continue;

            var update = Builders<Episode>.Update.Set(e => e.PlainDescription, plainDescription);
            await episodes.UpdateOneAsync(
                Builders<Episode>.Filter.Eq(e => e.Id, episode.Id),
                update,
                cancellationToken: cancellationToken);
        }
    }
}
