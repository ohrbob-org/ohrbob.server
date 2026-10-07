namespace Ohrbob.Web.Configuration;

public class AppVersionOptions
{
    public const string SectionName = "AppVersion";
    public string Version { get; set; } = "dev-local";
    public string Tag { get; set; } = "beta";
}
