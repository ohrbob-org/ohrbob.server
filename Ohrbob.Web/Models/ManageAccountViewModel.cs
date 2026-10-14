using Ohrbob.Server.Shared.Data;
using System.Collections.Generic;

namespace Ohrbob.Web.Models;

public class ManageAccountViewModel
{
    public Account Account { get; set; } = new();
    public SubscriptionTier Tier => Account.Tier;
    public TierLimits Limits { get; set; } = new();
    public int StationCount { get; set; }
    public int ProfileCount { get; set; }
    public int MaxFeedsPerStationUsed { get; set; }
    public List<ApiKey> ApiKeys { get; set; } = new();
    public List<Profile> Profiles { get; set; } = new();
}
