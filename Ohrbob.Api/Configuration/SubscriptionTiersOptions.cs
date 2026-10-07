using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Configuration;

public class SubscriptionTiersOptions
{
    public const string ConfigSection = "SubscriptionTiers";

    public TierLimits Free { get; set; } = new()
    {
        MaxStations = 2,
        MaxFeedsPerStation = 10,
        MaxProfiles = 1,
        StreamEnabled = false
    };

    public TierLimits Premium { get; set; } = new()
    {
        MaxStations = -1,
        MaxFeedsPerStation = -1,
        MaxProfiles = 5,
        StreamEnabled = true
    };

    public TierLimits For(SubscriptionTier tier) => tier == SubscriptionTier.Premium ? Premium : Free;
}
