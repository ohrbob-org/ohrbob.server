using MongoDB.Driver;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Migrations;

/// <summary>
/// 0.0.17 replaces the old first feed colour (a strong red) with the Ohrbob
/// orange. Feed colours are assigned automatically, so no user choice is lost.
/// Idempotent: only the legacy red is rewritten.
/// </summary>
public class Migration_0_0_17_OrangeFeedColours : IMigration
{
    private const string LegacyRed = "#E63946";
    private const string OhrbobOrange = "#FC5D00";

    public string Version => "0.0.17";

    public async Task ApplyAsync(IMongoDatabase database, CancellationToken cancellationToken = default)
    {
        var stations = database.GetCollection<Station>(typeof(Station).Name + "Collection");
        var withLegacy = Builders<Station>.Filter.ElemMatch(
            s => s.Schedule.Feeds,
            f => f.Color == LegacyRed);
        var affected = await stations.Find(withLegacy).ToListAsync(cancellationToken);

        foreach (var station in affected)
        {
            var changed = false;
            foreach (var feed in station.Schedule.Feeds)
            {
                if (feed.Color != LegacyRed) continue;
                feed.Color = OhrbobOrange;
                changed = true;
            }

            if (!changed) continue;
            await stations.UpdateOneAsync(
                Builders<Station>.Filter.Eq(s => s.Id, station.Id),
                Builders<Station>.Update.Set(s => s.Schedule, station.Schedule),
                cancellationToken: cancellationToken);
        }
    }
}
