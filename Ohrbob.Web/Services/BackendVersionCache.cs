namespace Ohrbob.Web.Services;

public class BackendVersionCache
{
    private readonly Lock _lock = new();
    public string ApiVersion { get; private set; } = "unknown";
    public string ApiTag { get; private set; } = "";
    public bool ApiReachable { get; private set; }
    public string PlayerVersion { get; private set; } = "unknown";
    public string PlayerTag { get; private set; } = "";
    public bool PlayerReachable { get; private set; }
    public DateTime LastCheckedUtc { get; private set; } = DateTime.MinValue;

    public void Update(string apiVersion, string apiTag, bool apiReachable,
        string playerVersion, string playerTag, bool playerReachable, DateTime checkedUtc)
    {
        lock (_lock)
        {
            ApiVersion = apiVersion; ApiTag = apiTag; ApiReachable = apiReachable;
            PlayerVersion = playerVersion; PlayerTag = playerTag; PlayerReachable = playerReachable;
            LastCheckedUtc = checkedUtc;
        }
    }
}
