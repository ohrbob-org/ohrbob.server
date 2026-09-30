namespace Ohrbob.Web.Configuration;

public class OhrbobApiOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = string.Empty;

    public void Initialize()
    {
        BaseUrl = BaseUrl.TrimEnd('/');
    }
}