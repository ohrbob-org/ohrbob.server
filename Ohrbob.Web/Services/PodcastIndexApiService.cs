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

    public virtual Task<List<PodcastSearchResult>> SearchAsync(string query)
        => GetFeedsAsync($"search/byterm?q={Uri.EscapeDataString(query)}");

    public virtual Task<List<PodcastSearchResult>> GetTrendingAsync(string category, int max = 20)
        => GetFeedsAsync($"podcasts/trending?cat={Uri.EscapeDataString(category)}&max={max}");

    public virtual async Task<List<string>> GetCategoriesAsync()
    {
        var json = await GetJsonAsync("categories/list");
        if (string.IsNullOrEmpty(json)) return new List<string>();

        try
        {
            return JsonSerializer.Deserialize<PodcastIndexCategoriesResponse>(json)?.Categories?
                .Select(c => c.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList() ?? new List<string>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing PodcastIndex categories.");
            return new List<string>();
        }
    }

    private async Task<List<PodcastSearchResult>> GetFeedsAsync(string path)
    {
        var json = await GetJsonAsync(path);
        if (string.IsNullOrEmpty(json)) return new List<PodcastSearchResult>();

        try
        {
            return JsonSerializer.Deserialize<PodcastIndexSearchResponse>(json)?.Feeds ?? new List<PodcastSearchResult>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing PodcastIndex response.");
            return new List<PodcastSearchResult>();
        }
    }

    private async Task<string?> GetJsonAsync(string path)
    {
        var apiKey = _configuration["PodcastIndex:ApiKey"];
        var apiSecret = _configuration["PodcastIndex:ApiSecret"];

        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
        {
            _logger.LogWarning("PodcastIndex API Key or Secret is missing.");
            return null;
        }

        var unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var hashSource = apiKey + apiSecret + unixTime;
        
        using var sha1 = SHA1.Create();
        var hashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(hashSource));
        var hashString = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("User-Agent", "OhrbobWeb/1.0");
        request.Headers.Add("X-Auth-Date", unixTime);
        request.Headers.Add("X-Auth-Key", apiKey);
        request.Headers.Add("Authorization", hashString);

        try
        {
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching from PodcastIndex API.");
            return null;
        }
    }
}