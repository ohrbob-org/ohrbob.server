using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Ohrbob.Server.Shared.Requests;
using Ohrbob.Web.Configuration;

namespace Ohrbob.Web.Services;

public class OhrbobApiService
{
    public OhrbobApiService(ILogger<OhrbobApiService> logger, IOptions<OhrbobApiOptions> options,IHttpClientFactory clientFactory)
    {
        
        ClientFactory = clientFactory;
        ApiOptions = options.Value;
        ApiOptions.Initialize();
    }

    public OhrbobApiOptions ApiOptions { get; set; }

    public IHttpClientFactory ClientFactory { get; set; }

    public async Task CreateOrUpdateAccountAsync(string name, string email, string picture, string identityProvider, string userId)
    {
        using var request = BuildRequest(HttpMethod.Post, "/account/create");
        
        request.Content = new StringContent(JsonSerializer.Serialize(new CreateAccountRequest()
        {
            Name = name,
            Email = email,
            ProfilePicture = picture,
            IdentityProvider = identityProvider,
            UserId = userId
        }), Encoding.UTF8, "application/json");
        using var client =  ClientFactory.CreateClient(nameof(OhrbobApiService));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
    
    
    protected HttpRequestMessage BuildRequest(HttpMethod method, string apiPath)
    {
        
        var request = new HttpRequestMessage(method,
            ApiOptions.BaseUrl + apiPath);
        request.Headers.Add("X-API-KEY", ApiOptions.ApiKey);
        request.Headers.Add("X-Version", ApiOptions.ApiVersion);
        return request;

    }
}