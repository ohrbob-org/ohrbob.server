using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Ohrbob.Web.Configuration;
using Ohrbob.Web.Services;

namespace Ohrbob.Web.Tests;

public class VersionPollingServiceTests
{
    private static Mock<HttpMessageHandler> CreateHandler(HttpResponseMessage response)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
        return handler;
    }

    private static VersionPollingService CreateService(
        HttpMessageHandler apiHandler,
        HttpMessageHandler playerHandler,
        BackendVersionCache cache)
    {
        var apiClient = new HttpClient(apiHandler) { BaseAddress = new Uri("https://api.test/") };
        var playerClient = new HttpClient(playerHandler) { BaseAddress = new Uri("https://player.test/") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(VersionPollingService.ApiClientName)).Returns(apiClient);
        factory.Setup(f => f.CreateClient(VersionPollingService.PlayerClientName)).Returns(playerClient);
        var options = Options.Create(new OhrbobApiOptions
        {
            BaseUrl = "https://api.test/",
            ApiKey = "key",
            PlayerApiUrl = "https://player.test/"
        });
        var logger = new Mock<ILogger<VersionPollingService>>();
        return new VersionPollingService(factory.Object, options, cache, logger.Object);
    }

    [Test]
    public async Task PollOnce_StoresVersionsAndTags_WhenBothReachable()
    {
        var apiHandler = CreateHandler(new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent("""{"version":"api-sha","tag":"beta"}""")
        });
        var playerHandler = CreateHandler(new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent("""{"version":"player-sha","tag":"beta"}""")
        });
        var cache = new BackendVersionCache();
        var service = CreateService(apiHandler.Object, playerHandler.Object, cache);

        await service.PollOnceAsync(CancellationToken.None);

        Assert.That(cache.ApiVersion, Is.EqualTo("api-sha"));
        Assert.That(cache.ApiTag, Is.EqualTo("beta"));
        Assert.That(cache.ApiReachable, Is.True);
        Assert.That(cache.PlayerVersion, Is.EqualTo("player-sha"));
        Assert.That(cache.PlayerTag, Is.EqualTo("beta"));
        Assert.That(cache.PlayerReachable, Is.True);
        Assert.That(cache.LastCheckedUtc, Is.Not.EqualTo(DateTime.MinValue));
    }

    [Test]
    public async Task PollOnce_MarksUnreachable_WhenApiThrows()
    {
        var apiHandler = new Mock<HttpMessageHandler>();
        apiHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("boom"));
        var playerHandler = CreateHandler(new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent("""{"version":"player-sha","tag":"beta"}""")
        });
        var cache = new BackendVersionCache();
        var service = CreateService(apiHandler.Object, playerHandler.Object, cache);

        await service.PollOnceAsync(CancellationToken.None);

        Assert.That(cache.ApiReachable, Is.False);
        Assert.That(cache.ApiVersion, Is.EqualTo("unknown"));
        Assert.That(cache.PlayerReachable, Is.True);
    }
}
