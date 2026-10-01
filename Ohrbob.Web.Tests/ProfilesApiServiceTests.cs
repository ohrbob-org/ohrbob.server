using NUnit.Framework;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Configuration;
using Ohrbob.Web.Services;

namespace Ohrbob.Web.Tests;

public class ProfilesApiServiceTests
{
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly ProfilesApiService _service;
    private readonly string _baseUri = "https://api.example.com";

    public ProfilesApiServiceTests()
    {
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(_httpMessageHandlerMock.Object)
        {
            BaseAddress = new Uri(_baseUri)
        };

        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _httpClientFactoryMock.Setup(x => x.CreateClient(nameof(OhrbobApiService)))
            .Returns(httpClient);

        var loggerMock = new Mock<ILogger<OhrbobApiService>>();
        var optionsMock = new Mock<IOptions<OhrbobApiOptions>>();
        optionsMock.Setup(x => x.Value).Returns(new OhrbobApiOptions
        {
            BaseUrl = _baseUri,
            ApiKey = "test-api-key"
        });

        _service = new ProfilesApiService(loggerMock.Object, optionsMock.Object, _httpClientFactoryMock.Object);
    }

    [Test]
    public async Task GetProfilesByAccountIdAsync_ReturnsProfiles()
    {
        var accountId = "acc-1";
        var expectedProfiles = new List<Profile> { new Profile { Id = "1", Name = "P1" } };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => 
                    req.Method == HttpMethod.Get && 
                    req.RequestUri.ToString() == $"{_baseUri}/profiles/account/{accountId}"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(expectedProfiles)
            });

        var result = await _service.GetProfilesByAccountIdAsync(accountId);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Name, Is.EqualTo("P1"));
    }
}
