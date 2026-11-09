using Microsoft.Extensions.Configuration;
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

    [Test]
    public void Options_Defaults_MatchSpec()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var options = config.GetSection(SubscriptionTiersOptions.ConfigSection).Get<SubscriptionTiersOptions>() ?? new SubscriptionTiersOptions();
        Assert.That(options.Free.MaxStations, Is.EqualTo(2));
        Assert.That(options.Free.MaxFeedsPerStation, Is.EqualTo(10));
        Assert.That(options.Free.MaxProfiles, Is.EqualTo(1));
        Assert.That(options.Free.StreamEnabled, Is.False);
        Assert.That(options.Morebob.MaxStations, Is.EqualTo(-1));
        Assert.That(options.Morebob.MaxFeedsPerStation, Is.EqualTo(-1));
        Assert.That(options.Morebob.MaxProfiles, Is.EqualTo(5));
        Assert.That(options.Morebob.StreamEnabled, Is.True);
    }

    [Test]
    public void Options_EnvOverride_Binds()
    {
        var values = new Dictionary<string, string?> { ["SubscriptionTiers:Free:MaxStations"] = "99" };
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var options = config.GetSection(SubscriptionTiersOptions.ConfigSection).Get<SubscriptionTiersOptions>()!;
        Assert.That(options.Free.MaxStations, Is.EqualTo(99));
    }
}
