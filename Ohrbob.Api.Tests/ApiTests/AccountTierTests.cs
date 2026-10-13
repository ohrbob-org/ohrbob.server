using NUnit.Framework;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Tests.ApiTests;

public class AccountTierTests
{
    [Test]
    public void TierLimits_For_Morebob_AllowsStream()
    {
        var options = new Ohrbob.Api.Configuration.SubscriptionTiersOptions();
        var morebob = options.For(SubscriptionTier.Morebob);
        Assert.That(morebob.StreamEnabled, Is.True);
        Assert.That(TierLimits.IsUnlimited(morebob.MaxStations), Is.True);
    }

    [Test]
    public void Account_DefaultsToFreeTier()
    {
        var account = new Account();
        Assert.That(account.Tier, Is.EqualTo(SubscriptionTier.Free));
        Assert.That(account.StripeCustomerId, Is.Null);
    }
}
