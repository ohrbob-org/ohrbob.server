using NUnit.Framework;
using Ohrbob.Player.Health;

namespace Ohrbob.Player.Tests;

public class PlayerHealthStatusTests
{
    [Test]
    public void GetMongoStatus_NullDatabase_ReturnsNotConfigured()
    {
        Assert.That(PlayerHealthStatus.GetMongoStatus(null), Is.EqualTo("NotConfigured"));
    }
}
