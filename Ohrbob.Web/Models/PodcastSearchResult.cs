using System.Text.Json.Serialization;

namespace Ohrbob.Web.Models;

public class PodcastSearchResult
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("image")]
    public string Image { get; set; } = string.Empty;
}

public class PodcastIndexSearchResponse
{
    [JsonPropertyName("feeds")]
    public List<PodcastSearchResult>? Feeds { get; set; }
}