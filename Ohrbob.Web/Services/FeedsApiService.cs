using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Server.Shared.Requests;
using Ohrbob.Web.Configuration;

namespace Ohrbob.Web.Services;

/// <summary>Outcome of asking the API to refresh a feed. Never throws for a rejected refresh.</summary>
public sealed record FeedRefreshApiResult(FeedRefreshResultDto? Feed, string? Error);

public class FeedsApiService : OhrbobApiService
{
    public FeedsApiService(ILogger<OhrbobApiService> logger, IOptions<OhrbobApiOptions> options, IHttpClientFactory clientFactory)
        : base(logger, options, clientFactory)
    {
    }

    public virtual async Task<List<Feed>?> GetFeedsAsync()
    {
        using var request = BuildRequest(HttpMethod.Get, "/feeds");
        using var client = ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<Feed>>();
    }

    public virtual async Task<FeedRefreshApiResult> RefreshFeedAsync(string feedId)
    {
        using var request = BuildRequest(HttpMethod.Post, $"/feeds/{Uri.EscapeDataString(feedId)}/refresh");
        using var client = ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            var feed = await response.Content.ReadFromJsonAsync<FeedRefreshResultDto>();
            return new FeedRefreshApiResult(
                feed,
                feed == null ? "The feed returned no data." : null);
        }

        string? detail = null;
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
            detail = problem?.Detail ?? problem?.Title;
        }
        catch
        {
            // The API did not answer with a problem document; fall back to the status.
        }

        return new FeedRefreshApiResult(
            null,
            string.IsNullOrWhiteSpace(detail)
                ? $"The feed could not be refreshed ({(int)response.StatusCode})."
                : detail);
    }
}
