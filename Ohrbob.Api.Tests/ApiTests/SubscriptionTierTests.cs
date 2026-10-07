using NUnit.Framework;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Tests.ApiTests;

public class SubscriptionTierTests
{
    [Test]
    public void Free_IsDefaultTier()
    {
        var account = new Account();
        Assert.That(account.Tier, Is.EqualTo(SubscriptionTier.Free));
    }

    [Test]
    public void IsUnlimited_MinusOne_IsTrue()
    {
        Assert.That(TierLimits.IsUnlimited(-1), Is.True);
        Assert.That(TierLimits.IsUnlimited(0), Is.False);
        Assert.That(TierLimits.IsUnlimited(2), Is.False);
    }
}
