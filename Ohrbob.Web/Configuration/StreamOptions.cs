namespace Ohrbob.Web.Configuration;

public class StreamOptions
{
    public const string SectionName = "Stream";

    /// <summary>Browser-facing base URL of the stream service (e.g. https://stream.ohrbob.org).</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:5005";

    public string GetStationUrl(string accountId, string stationId, string streamToken) =>
        $"{PublicBaseUrl.TrimEnd('/')}/{accountId}/{stationId}?key={streamToken}";
}
