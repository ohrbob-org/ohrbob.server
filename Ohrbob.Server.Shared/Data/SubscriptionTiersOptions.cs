namespace Ohrbob.Server.Shared.Data;

public class SubscriptionTiersOptions
{
    public const string ConfigSection = "SubscriptionTiers";

    public TierLimits Free { get; set; } = new()
    {
        MaxStations = 2,
        MaxFeedsPerStation = 10,
        MaxProfiles = 1,
        StreamEnabled = false,
        MaxConcurrentListeners = 0
    };

    public TierLimits Morebob { get; set; } = new()
    {
        MaxStations = -1,
        MaxFeedsPerStation = -1,
        MaxProfiles = 5,
        StreamEnabled = true,
        MaxConcurrentListeners = 3
    };

    // shortcut: Premium aliases only the listener limit; use Morebob for the other limits.
    public TierLimits? Premium { get; set; }

    public TierLimits For(SubscriptionTier tier)
    {
        if (tier != SubscriptionTier.Morebob) return Free;
        if (Premium is { MaxConcurrentListeners: >= 0 } premium)
        {
            Morebob.MaxConcurrentListeners = premium.MaxConcurrentListeners;
        }
        return Morebob;
    }
}
