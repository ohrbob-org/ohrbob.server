namespace Ohrbob.Web.Models;

public class AboutViewModel
{
    public string WebVersion { get; set; } = string.Empty;
    public string WebTag { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = string.Empty;
    public string ApiTag { get; set; } = string.Empty;
    public bool ApiReachable { get; set; }
    public string PlayerVersion { get; set; } = string.Empty;
    public string PlayerTag { get; set; } = string.Empty;
    public bool PlayerReachable { get; set; }
    public DateTime LastCheckedUtc { get; set; }
}
