using MongoDB.Driver;

namespace Ohrbob.Player.Health;

public static class PlayerHealthStatus
{
    public static string GetMongoStatus(IMongoDatabase? database) =>
        database is null ? "NotConfigured" : "Configured";
}
