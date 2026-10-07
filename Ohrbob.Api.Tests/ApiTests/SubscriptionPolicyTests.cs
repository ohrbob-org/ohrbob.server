using Microsoft.Extensions.Options;
using NUnit.Framework;
using Ohrbob.Api.Configuration;
using Ohrbob.Api.Services;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Tests.ApiTests;

public class SubscriptionPolicyTests
{
    private static SubscriptionPolicy Create(TierLimits free, TierLimits premium)
    {
        var options = Options.Create(new SubscriptionTiersOptions { Free = free, Premium = premium });
        return new SubscriptionPolicy(options);
    }

    [Test]
    public void Free_AllowsUpToTwoStations()
    {
        var policy = Create(
            new TierLimits { MaxStations = 2, MaxFeedsPerStation = 10, MaxProfiles = 1 },
            new TierLimits { MaxStations = -1, MaxFeedsPerStation = -1, MaxProfiles = 5, StreamEnabled = true });
        var limits = policy.For(new Account { Tier = SubscriptionTier.Free });
        Assert.That(policy.CanCreateStation(1, limits), Is.True);
        Assert.That(policy.CanCreateStation(2, limits), Is.False);
    }

    [Test]
    public void Premium_UnlimitedStations()
    {
        var policy = Create(
            new TierLimits { MaxStations = 2, MaxFeedsPerStation = 10, MaxProfiles = 1 },
            new TierLimits { MaxStations = -1, MaxFeedsPerStation = -1, MaxProfiles = 5, StreamEnabled = true });
        var limits = policy.For(new Account { Tier = SubscriptionTier.Premium });
        Assert.That(policy.CanCreateStation(1000, limits), Is.True);
        Assert.That(policy.CanStream(limits), Is.True);
    }
}
