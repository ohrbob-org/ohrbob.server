using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Configuration;

namespace Ohrbob.Web.Services;

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
}
