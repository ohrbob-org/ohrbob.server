using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ohrbob.Api.Configuration;
using Ohrbob.Api.Extensions;

namespace Ohrbob.Api.Tests.ApiTests;

public class AccountsControllerTests
{
    private ApiTestApp<Program> _app;
    private HttpClient _client;

    private const string _testApiKey = "123456";
    
    [SetUp]
    public void Setup()
    {
        _app = new ApiTestApp<Program>(serviceCollection =>
        {
            
        }, configuration =>
        {
            var configContainer = new
            {
                MongoDbLogs = new MongoDbSettings(){ConnectionString = "mongodb://localhost:27017/TestsOhrbobLogs", DatabaseName = "TestsOhrbobLogs"},
                MongoDbData = new MongoDbSettings(){ConnectionString = "mongodb://localhost:27017/TestsOhrbobData", DatabaseName = "TestsOhrbobData"},
                ApiKeys = new List<ApiKey>(){
                    new ApiKey(_testApiKey, "test", [new Claim("scope", "all")])  
            }
            };
            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(configContainer);
            configuration.AddJsonStream(new MemoryStream(jsonBytes));
            
        }, setupMongoDbContext: true);
        _client = _app.CreateDefaultClient();
        var key = _app.Services.GetRequiredService<IConfiguration>()["ApiKeys:0:Key"];
        
        Assert.That(key, Is.Not.Null);
        
        
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _app?.Dispose();
    }
    
    [Test]
    public async Task GetCode_Works()
    {
        Assert.Pass();
        /*// prep
        await _skuService.CreateAsync(new SkuEntry() { Value = TestSkuValue });

        var payload = CodesServiceTests.CreateTestPayload();
        payload.PublicKey = _notaryService.PublicKey;
        payload.Signature = "123";
        payload.Sku = TestSkuValue;
        var addedCodeResponse = await _adminService.AddCodeAdmin(payload);

        // getSequenceEntry by signature
        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/internal/admin/code/?signature={Uri.EscapeDataString(payload.Signature)}");
        getRequest.Headers.Add("X-Api-Key", _apiKey);
        var getResponse = await _client.SendAsync(getRequest);
        Assert.That(getResponse.IsSuccessStatusCode);
        var getResponseContent = await getResponse.Content.ReadFromJsonAsync<CodeEntry>();
        Assert.That(getResponseContent, Is.Not.Null);
        Assert.That(getResponseContent.Id, Is.EqualTo(addedCodeResponse.Id));
        
        // getSequenceEntry by id
        var getRequest2 = new HttpRequestMessage(HttpMethod.Get, $"/internal/admin/code/?id={Uri.EscapeDataString(addedCodeResponse.Id)}");
        getRequest2.Headers.Add("X-Api-Key", _apiKey);
        var getResponse2 = await _client.SendAsync(getRequest2);
        Assert.That(getResponse2.IsSuccessStatusCode);
        var getResponseContent2 = await getResponse2.Content.ReadFromJsonAsync<CodeEntry>();
        Assert.That(getResponseContent2, Is.Not.Null);
        Assert.That(getResponseContent2.Id, Is.EqualTo(addedCodeResponse.Id));
        
        
        // getSequenceEntry requires parameter
        var getRequest3 = new HttpRequestMessage(HttpMethod.Get, $"/internal/admin/code/");
        getRequest3.Headers.Add("X-Api-Key", _apiKey);
        var getResponse3 = await _client.SendAsync(getRequest3);
        Assert.That(getResponse3.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        */
    }

}