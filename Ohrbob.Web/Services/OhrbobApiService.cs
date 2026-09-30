using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Server.Shared.Requests;
using Ohrbob.Web.Configuration;

namespace Ohrbob.Web.Services;

public abstract class OhrbobApiService
{
    public OhrbobApiService(ILogger<OhrbobApiService> logger, IOptions<OhrbobApiOptions> options,IHttpClientFactory clientFactory)
    {
        
        ClientFactory = clientFactory;
        ApiOptions = options.Value;
        ApiOptions.Initialize();
    }

    public OhrbobApiOptions ApiOptions { get; set; }

    public IHttpClientFactory ClientFactory { get; set; }

    protected HttpRequestMessage BuildRequest(HttpMethod method, string apiPath)
    {
        var request = new HttpRequestMessage(method,
            ApiOptions.BaseUrl + apiPath);
        request.Headers.Add("X-API-KEY", ApiOptions.ApiKey);
        request.Headers.Add("X-Version", ApiOptions.ApiVersion);
        return request;

    }
}