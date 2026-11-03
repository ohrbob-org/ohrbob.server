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
    private Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private Mock<IHttpClientFactory> _httpClientFactoryMock;
    private ProfilesApiService _service;
    private readonly string _baseUri = "https://api.example.com";

    [SetUp]
    public void Setup()
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

    [Test]
    public void CreateProfileAsync_ThrowsLimitExceededException_OnBadRequest()
    {
        var accountId = "acc-1";
        var profile = new Profile { Name = "New Profile" };
        var problemDetails = new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Title = "Profile limit reached",
            Detail = "Cannot create more profiles."
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => 
                    req.Method == HttpMethod.Post && 
                    req.RequestUri!.ToString() == $"{_baseUri}/profiles/{accountId}"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.BadRequest,
                Content = JsonContent.Create(problemDetails)
            });

        var ex = Assert.ThrowsAsync<LimitExceededException>(() => _service.CreateProfileAsync(accountId, profile));
        Assert.That(ex.Message, Is.EqualTo("Cannot create more profiles."));
    }

    [Test]
    public void DeleteProfileAsync_ThrowsLimitExceededException_OnBadRequest()
    {
        var profileId = "prof-1";
        var problemDetails = new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Title = "Profile Delete Error",
            Detail = "Cannot delete the last remaining profile of an account."
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => 
                    req.Method == HttpMethod.Delete && 
                    req.RequestUri!.ToString() == $"{_baseUri}/profiles/{profileId}"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.BadRequest,
                Content = JsonContent.Create(problemDetails)
            });

        var ex = Assert.ThrowsAsync<LimitExceededException>(() => _service.DeleteProfileAsync(profileId));
        Assert.That(ex.Message, Is.EqualTo("Cannot delete the last remaining profile of an account."));
    }
}
