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
        var missing = await stations
            .Find(Builders<Station>.Filter.Eq(s => s.ShareCode, string.Empty))
            .ToListAsync(cancellationToken);

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
