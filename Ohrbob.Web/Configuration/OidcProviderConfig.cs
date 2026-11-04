namespace Ohrbob.Web.Configuration;

public static class OidcProviderConfig
{
    public static readonly string[] All = ["Nextcloud", "Google", "GitHub"];

    public static bool IsEnabled(IConfiguration config, string provider)
    {
        var section = config.GetSection($"Oidc:{provider}");
        if (string.IsNullOrWhiteSpace(section["ClientId"]) || string.IsNullOrWhiteSpace(section["ClientSecret"]))
        {
            return false;
        }

        // OIDC providers also need an Authority; GitHub (OAuth) has none.
        var authority = section["Authority"];
        return authority is null || !string.IsNullOrWhiteSpace(authority);
    }

    public static string[] Enabled(IConfiguration config) => All.Where(p => IsEnabled(config, p)).ToArray();
}
