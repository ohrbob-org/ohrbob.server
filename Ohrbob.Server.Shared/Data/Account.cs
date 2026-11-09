using System;
using System.Collections.Generic;

namespace Ohrbob.Server.Shared.Data;

public class Account : DbEntry
{
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ProfilePicture { get; set; } = string.Empty;
    public List<IdpUser> IdentityProviders { get; set; } = new();
    public string LastUsedProfileId { get; set; } = string.Empty;
    public bool NeedsOnboarding { get; set; } = true;
    public SubscriptionTier Tier { get; set; } = SubscriptionTier.Free;
    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public string? StripeSubscriptionStatus { get; set; }
    public DateTime? StripeCurrentPeriodEnd { get; set; }
    public DateTime? StripeCancelAt { get; set; }
    public DateTime? StripeSubscriptionEndedAt { get; set; }
    public string? LastSeenChangelog { get; set; }
}