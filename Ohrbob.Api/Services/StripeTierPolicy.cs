using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Services;

public static class StripeTierPolicy
{
    /// <summary>
    /// Morebob stays active while Stripe is collecting payment or retrying a failed
    /// one. A cancelled subscription keeps its tier until the paid period ends; Stripe
    /// only reports it as canceled (customer.subscription.deleted) at that point.
    /// </summary>
    public static SubscriptionTier ForSubscriptionStatus(string? status) => status switch
    {
        "active" or "trialing" or "past_due" => SubscriptionTier.Morebob,
        _ => SubscriptionTier.Free
    };
}
