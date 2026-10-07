using Microsoft.Extensions.Options;
using Ohrbob.Api.Configuration;
using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Api.Services;

public class SubscriptionPolicy
{
    private readonly SubscriptionTiersOptions _options;

    public SubscriptionPolicy(IOptions<SubscriptionTiersOptions> options)
    {
        _options = options.Value;
    }

    public TierLimits For(Account? account) =>
        _options.For(account?.Tier ?? SubscriptionTier.Free);

    public bool CanCreateStation(int currentStationCount, TierLimits limits) =>
        TierLimits.IsUnlimited(limits.MaxStations) || currentStationCount < limits.MaxStations;

    public bool CanSetFeedCount(int feedCount, TierLimits limits) =>
        TierLimits.IsUnlimited(limits.MaxFeedsPerStation) || feedCount <= limits.MaxFeedsPerStation;

    public bool CanCreateProfile(int currentProfileCount, TierLimits limits) =>
        TierLimits.IsUnlimited(limits.MaxProfiles) || currentProfileCount < limits.MaxProfiles;

    public bool CanStream(TierLimits limits) => limits.StreamEnabled;
}
