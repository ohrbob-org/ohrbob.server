namespace Ohrbob.Web.Configuration;

public class OhrbobApiOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = string.Empty;
    public string PlayerApiUrl { get; set; } = string.Empty;
    /// <summary>Browser-facing Player base URL (e.g. https://player.ohrbob.org). Falls back to <see cref="PlayerApiUrl"/> when empty.</summary>
    public string PlayerPublicUrl { get; set; } = string.Empty;

    /// <summary>URL exposed to the frontend audio widget.</summary>
    public string FrontendPlayerUrl => string.IsNullOrWhiteSpace(PlayerPublicUrl) ? PlayerApiUrl : PlayerPublicUrl;

    public void Initialize()
    {
        BaseUrl = BaseUrl.TrimEnd('/');
        PlayerApiUrl = PlayerApiUrl.TrimEnd('/');
        PlayerPublicUrl = PlayerPublicUrl.TrimEnd('/');
    }
}