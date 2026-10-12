namespace Ohrbob.Web.Configuration;

public class HtmxNavigationOptions
{
    public const string SectionName = "HtmxNavigation";
    public List<string> EnabledEndpoints { get; set; } = new();
}
