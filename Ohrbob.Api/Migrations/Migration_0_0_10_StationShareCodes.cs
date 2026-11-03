using MongoDB.Driver;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Server.Shared.Helpers;

namespace Ohrbob.Api.Migrations;

/// <summary>
/// 0.0.10 introduced <see cref="Station.ShareCode"/>, the short opaque identifier used by the
/// public share URL. Existing stations have no code yet, so this migration backfills one and
/// adds the lookup index. It is idempotent: stations that already have a code are left alone.
/// </summary>
public class Migration_0_0_10_StationShareCodes : IMigration
{
    public string Version => "0.0.10";

    public async Task ApplyAsync(IMongoDatabase database, CancellationToken cancellationToken = default)
    {
        var stations = database.GetCollection<Station>(typeof(Station).Name + "Collection");
        // Stations created before this release have no ShareCode field at all, so match both
        // missing/null and empty values. The DeletedAt check keeps soft-deleted rows untouched.
        var missingFilter = Builders<Station>.Filter.And(
            Builders<Station>.Filter.Eq(s => s.DeletedAt, null),
            Builders<Station>.Filter.Or(
                Builders<Station>.Filter.Eq(s => s.ShareCode, null),
                Builders<Station>.Filter.Eq(s => s.ShareCode, string.Empty)));
        var missing = await stations.Find(missingFilter).ToListAsync(cancellationToken);

        foreach (var station in missing)
        {
            await stations.UpdateOneAsync(
                Builders<Station>.Filter.Eq(s => s.Id, station.Id),
                Builders<Station>.Update.Set(s => s.ShareCode, ShareCodeGenerator.Generate()),
                cancellationToken: cancellationToken);
        }

        await stations.Indexes.CreateOneAsync(
            new CreateIndexModel<Station>(
                Builders<Station>.IndexKeys.Ascending(s => s.ShareCode),
                new CreateIndexOptions { Name = "shareCode" }),
            cancellationToken: cancellationToken);
    }
}
