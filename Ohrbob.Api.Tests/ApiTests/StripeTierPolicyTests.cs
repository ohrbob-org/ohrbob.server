using NUnit.Framework;
using Ohrbob.Api.Services;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Tests.ApiTests;

public class StripeTierPolicyTests
{
    [TestCase("active", SubscriptionTier.Morebob)]
    [TestCase("trialing", SubscriptionTier.Morebob)]
    [TestCase("past_due", SubscriptionTier.Morebob)]
    [TestCase("canceled", SubscriptionTier.Free)]
    [TestCase("unpaid", SubscriptionTier.Free)]
    [TestCase("incomplete", SubscriptionTier.Free)]
    [TestCase("incomplete_expired", SubscriptionTier.Free)]
    [TestCase(null, SubscriptionTier.Free)]
    public void Status_MapsToTier(string? status, SubscriptionTier expected)
    {
        Assert.That(StripeTierPolicy.ForSubscriptionStatus(status), Is.EqualTo(expected));
    }
}
