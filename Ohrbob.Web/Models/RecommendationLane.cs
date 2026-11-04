namespace Ohrbob.Web.Models;

public class RecommendationLane
{
    public string Category { get; set; } = string.Empty;
    public List<PodcastSearchResult> Podcasts { get; set; } = new();
}
