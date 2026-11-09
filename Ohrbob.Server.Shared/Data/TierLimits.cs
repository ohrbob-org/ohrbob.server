namespace Ohrbob.Server.Shared.Data;

public class TierLimits
{
    public int MaxStations { get; set; } = -1;
    public int MaxFeedsPerStation { get; set; } = -1;
    public int MaxProfiles { get; set; } = -1;
    public bool StreamEnabled { get; set; }
    public int MaxConcurrentListeners { get; set; } = -1;

    public static bool IsUnlimited(int value) => value < 0;
}
