namespace Ohrbob.Web.Configuration;

public class LegalOptions
{
    public const string SectionName = "Legal";

    public bool Show { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    public bool IsConfigured => Show;
}
