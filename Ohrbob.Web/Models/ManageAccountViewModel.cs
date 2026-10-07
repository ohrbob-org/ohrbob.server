using Ohrbob.Server.Shared.Data;

namespace Ohrbob.Web.Models;

public class ManageAccountViewModel
{
    public Account Account { get; set; } = new();
    public SubscriptionTier Tier => Account.Tier;
    public TierLimits Limits { get; set; } = new();
    public int StationCount { get; set; }
    public int ProfileCount { get; set; }
    public int MaxFeedsPerStationUsed { get; set; }
}
