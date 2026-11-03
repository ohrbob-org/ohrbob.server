using MongoDB.Driver;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Migrations;

/// <summary>
/// 0.0.12 introduced Host Pace. Existing stations have no fields yet; write explicit defaults
/// so old documents have the same shape as new ones. Idempotent: only missing fields are set.
/// </summary>
public class Migration_0_0_12_HostPaceDefaults : IMigration
{
    public string Version => "0.0.12";

    public async Task ApplyAsync(IMongoDatabase database, CancellationToken cancellationToken = default)
    {
        var stations = database.GetCollection<Station>(typeof(Station).Name + "Collection");

        await stations.UpdateManyAsync(
            Builders<Station>.Filter.Exists(s => s.HostPaceEnabled, false),
            Builders<Station>.Update.Set(s => s.HostPaceEnabled, false),
            cancellationToken: cancellationToken);

        await stations.UpdateManyAsync(
            Builders<Station>.Filter.Exists(s => s.HostProfileId, false),
            Builders<Station>.Update.Set(s => s.HostProfileId, null),
            cancellationToken: cancellationToken);
    }
}
