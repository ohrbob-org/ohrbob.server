using MongoDB.Driver;

namespace Ohrbob.Api.Migrations;

/// <summary>
/// Repairs databases that recorded 0.0.10 before the backfill filter was corrected. The
/// original filter only matched an explicitly empty ShareCode and missed legacy documents
/// where the field did not exist. Re-running the (now correct) idempotent backfill heals them.
/// </summary>
public class Migration_0_0_11_RepairStationShareCodes : IMigration
{
    public string Version => "0.0.11";

    public Task ApplyAsync(IMongoDatabase database, CancellationToken cancellationToken = default)
    {
        return new Migration_0_0_10_StationShareCodes().ApplyAsync(database, cancellationToken);
    }
}
