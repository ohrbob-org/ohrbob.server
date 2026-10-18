using System.Text;
using System.Text.Json;

namespace Ohrbob.E2ETests.Infrastructure;

/// <summary>
/// Client for the local <c>podcast-testcasts</c> fake podcast host that ships in
/// <c>testdata/ohrbob-testcasts</c>. It serves deterministic RSS feeds whose episode
/// publication dates follow the order of the configured episode array (newest first,
/// one day older per following episode), plus silent MP3s with range support.
/// Using it keeps playback-order tests independent of real publishers and the network.
/// </summary>
public static class TestCasts
{
    /// <summary>
    /// Base URL used by the test process itself to load fixtures (<c>POST /config</c>) and
    /// probe <c>/health</c>. Override with <c>OHRBOB_TESTCASTS_URL</c>.
    /// </summary>
    public static string ConfigureBaseUrl { get; } = Resolve("OHRBOB_TESTCASTS_URL", "http://localhost:5004");

    /// <summary>
    /// Base URL baked into the station's feed URLs, i.e. as reachable by the Api/Player under
    /// test. Defaults to <see cref="ConfigureBaseUrl"/>; override with
    /// <c>OHRBOB_TESTCASTS_FEED_URL</c> when the API runs in Docker and reaches the testcasts
    /// service by name, e.g. <c>http://testcasts:5004</c>.
    /// </summary>
    public static string FeedBaseUrl { get; } = Resolve("OHRBOB_TESTCASTS_FEED_URL", ConfigureBaseUrl);

    private static string Resolve(string variable, string fallback)
    {
        var configured = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrWhiteSpace(configured) ? fallback.TrimEnd('/') : configured.TrimEnd('/');
    }

    /// <summary>True when the testcasts server answers <c>/health</c>.</summary>
    public static async Task<bool> IsAvailableAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var response = await client.GetAsync($"{ConfigureBaseUrl}/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary><c>POST /config</c> — replaces the server's whole configuration.</summary>
    public static async Task ConfigureAsync(params TestCastPodcast[] podcasts)
    {
        // The server matches JSON keys case-sensitively (slug/name/episodes/title/mp3Slug),
        // so build the payload explicitly instead of relying on serializer casing.
        var payload = podcasts.Select(p => new
        {
            slug = p.Slug,
            name = p.Name,
            description = p.Description ?? string.Empty,
            coverUrl = string.Empty,
            episodes = p.Episodes.Select(e => new
            {
                title = e.Title,
                description = e.Description ?? string.Empty,
                coverUrl = string.Empty,
                mp3Slug = e.Mp3Slug
            }).ToArray()
        }).ToArray();

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"{ConfigureBaseUrl}/config", content);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"podcast-testcasts rejected the configuration ({(int)response.StatusCode}): {body}");
        }
    }

    /// <summary>The RSS URL for a configured podcast slug, as the Api/Player should reach it.</summary>
    public static string FeedUrl(string slug) => $"{FeedBaseUrl}/{slug}.rss";
}

public sealed record TestCastEpisode(string Title, string Mp3Slug, string? Description = null);

public sealed record TestCastPodcast(string Slug, string Name, IReadOnlyList<TestCastEpisode> Episodes, string? Description = null);
