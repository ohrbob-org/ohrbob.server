namespace Ohrbob.Api.Configuration;

public class ApiServerConfiguration
{
    public const string ConfigSection = nameof(ApiServerConfiguration);
    public const int MinimumJwtKeyLength = 32;

    public string Version { get; set; } = "dev-local";
    public string Tag { get; set; } = "beta";

    public string? ServerUrl { get; set; }

    public static ApiServerConfiguration GetFromConfig(IConfiguration configuration) => configuration.GetSection(ConfigSection).Get<ApiServerConfiguration>() ?? new ApiServerConfiguration();

    public void Validate()
    {
        if(string.IsNullOrWhiteSpace(Version))
            throw new Exception("Version is required in Configuration Section: " + ConfigSection);
        if(string.IsNullOrWhiteSpace(Tag))
            throw new Exception("Tag is required in Configuration Section: " + ConfigSection);
    }
}
