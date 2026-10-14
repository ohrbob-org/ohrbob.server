using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Configuration;

namespace Ohrbob.Web.Services;

public class ApiKeysApiService : OhrbobApiService
{
    public ApiKeysApiService(ILogger<OhrbobApiService> logger, IOptions<OhrbobApiOptions> options, IHttpClientFactory clientFactory) : base(logger, options, clientFactory)
    {
    }

    public virtual async Task<List<ApiKey>?> GetApiKeysByAccountIdAsync(string accountId)
    {
        using var request = BuildRequest(HttpMethod.Get, $"/apikeys/account/{accountId}");
        using var client = ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<ApiKey>>();
    }

    public virtual async Task<ApiKey?> CreateApiKeyAsync(string accountId, ApiKey apiKey)
    {
        using var request = BuildRequest(HttpMethod.Post, $"/apikeys/account/{accountId}");
        request.Content = new StringContent(JsonSerializer.Serialize(apiKey), Encoding.UTF8, "application/json");
        using var client = ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ApiKey>();
    }

    public virtual async Task DeleteApiKeyAsync(string id)
    {
        using var request = BuildRequest(HttpMethod.Delete, $"/apikeys/{id}");
        using var client = ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
}
