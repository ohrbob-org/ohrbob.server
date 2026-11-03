using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Configuration;

namespace Ohrbob.Web.Services;

public class ProfilesApiService : OhrbobApiService
{
    public ProfilesApiService(ILogger<OhrbobApiService> logger, IOptions<OhrbobApiOptions> options, IHttpClientFactory clientFactory) : base(logger, options, clientFactory)
    {
    }

    public virtual async Task<List<Profile>?> GetProfilesByAccountIdAsync(string accountId)
    {
        using var request = BuildRequest(HttpMethod.Get, $"/profiles/account/{accountId}");
        using var client = ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<Profile>>();
    }

    public virtual async Task<Profile?> GetProfileByIdAsync(string id)
    {
        using var request = BuildRequest(HttpMethod.Get, $"/profiles/{id}");
        using var client = ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Profile>();
    }

    public virtual async Task<Profile?> CreateProfileAsync(string accountId, Profile profile)
    {
        using var request = BuildRequest(HttpMethod.Post, $"/profiles/{accountId}");
        request.Content = new StringContent(JsonSerializer.Serialize(profile), Encoding.UTF8, "application/json");
        using var client = ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);
        
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            string detail = "Profile limit reached";
            try
            {
                var content = await response.Content.ReadAsStringAsync();
                if (content.StartsWith("{"))
                {
                    var problem = JsonSerializer.Deserialize<Microsoft.AspNetCore.Mvc.ProblemDetails>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (problem != null)
                    {
                        detail = !string.IsNullOrEmpty(problem.Detail) ? problem.Detail : 
                                 !string.IsNullOrEmpty(problem.Title) ? problem.Title : "Profile limit reached";
                    }
                }
                else if (!string.IsNullOrWhiteSpace(content))
                {
                    // For raw string or "string"
                    detail = content.Trim('\"');
                }
            }
            catch {}
            throw new LimitExceededException(detail);
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Profile>();
    }

    public virtual async Task<Profile?> UpdateProfileAsync(Profile profile)
    {
        using var request = BuildRequest(HttpMethod.Put, $"/profiles");
        request.Content = new StringContent(JsonSerializer.Serialize(profile), Encoding.UTF8, "application/json");
        using var client = ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Profile>();
    }

    public virtual async Task DeleteProfileAsync(string profileId)
    {
        using var request = BuildRequest(HttpMethod.Delete, $"/profiles/{profileId}");
        using var client = ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);
        
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            string detail = "Cannot delete profile";
            try
            {
                var content = await response.Content.ReadAsStringAsync();
                if (content.StartsWith("{"))
                {
                    var problem = JsonSerializer.Deserialize<Microsoft.AspNetCore.Mvc.ProblemDetails>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (problem != null)
                    {
                        detail = !string.IsNullOrEmpty(problem.Detail) ? problem.Detail : 
                                 !string.IsNullOrEmpty(problem.Title) ? problem.Title : detail;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(content))
                {
                    detail = content.Trim('\"');
                }
            }
            catch {}
            throw new LimitExceededException(detail); // Reuse the same exception type for simplicity
        }

        response.EnsureSuccessStatusCode();
    }

    public virtual async Task AddStationToProfileAsync(string profileId, string stationId)
    {
        using var request = BuildRequest(HttpMethod.Post, $"/profiles/{profileId}/stations/{stationId}");
        using var client = ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public virtual async Task RemoveStationFromProfileAsync(string profileId, string stationId)
    {
        using var request = BuildRequest(HttpMethod.Delete, $"/profiles/{profileId}/stations/{stationId}");
        using var client = ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public virtual async Task SubscribeToStationAsync(string profileId, string stationId)
    {
        using var request = BuildRequest(HttpMethod.Post, $"/profiles/{profileId}/subscriptions/{stationId}");
        using var client = ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public virtual async Task UnsubscribeFromStationAsync(string profileId, string stationId)
    {
        using var request = BuildRequest(HttpMethod.Delete, $"/profiles/{profileId}/subscriptions/{stationId}");
        using var client = ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
}
