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
}
