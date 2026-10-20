namespace Ohrbob.Web.Models;

public class ChangelogViewModel
{
    public bool ShowChangelog { get; set; }
    public string Version { get; set; } = string.Empty;
    public string ChangelogHtml { get; set; } = string.Empty;
}
