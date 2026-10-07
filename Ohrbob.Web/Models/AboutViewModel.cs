namespace Ohrbob.Web.Models;

public class AboutViewModel
{
    public string WebVersion { get; set; } = "unknown";
    public string WebTag { get; set; } = "";
    public string ApiVersion { get; set; } = "unknown";
    public string ApiTag { get; set; } = "";
    public bool ApiReachable { get; set; }
    public string PlayerVersion { get; set; } = "unknown";
    public string PlayerTag { get; set; } = "";
    public bool PlayerReachable { get; set; }
    public DateTime LastCheckedUtc { get; set; }
}
