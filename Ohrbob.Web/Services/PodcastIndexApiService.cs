using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ohrbob.Web.Models;

namespace Ohrbob.Web.Services;

public class PodcastIndexApiService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PodcastIndexApiService> _logger;

    public PodcastIndexApiService(HttpClient httpClient, IConfiguration configuration, ILogger<PodcastIndexApiService> logger)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri("https://api.podcastindex.org/api/1.0/");
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<List<PodcastSearchResult>> SearchAsync(string query)
    {
        var apiKey = _configuration["PodcastIndex:ApiKey"];
        var apiSecret = _configuration["PodcastIndex:ApiSecret"];

        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
        {
            _logger.LogWarning("PodcastIndex API Key or Secret is missing.");
            return new List<PodcastSearchResult>();
        }

        var unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var hashSource = apiKey + apiSecret + unixTime;
        
        using var sha1 = SHA1.Create();
        var hashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(hashSource));
        var hashString = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

        var request = new HttpRequestMessage(HttpMethod.Get, $"search/byterm?q={Uri.EscapeDataString(query)}");
        request.Headers.Add("User-Agent", "OhrbobWeb/1.0");
        request.Headers.Add("X-Auth-Date", unixTime);
        request.Headers.Add("X-Auth-Key", apiKey);
        request.Headers.Add("Authorization", hashString);

        try
        {
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var searchResponse = JsonSerializer.Deserialize<PodcastIndexSearchResponse>(content);

            return searchResponse?.Feeds ?? new List<PodcastSearchResult>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching from PodcastIndex API.");
            return new List<PodcastSearchResult>();
        }
    }
}