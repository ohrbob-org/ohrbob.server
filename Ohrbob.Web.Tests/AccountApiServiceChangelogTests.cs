using NUnit.Framework;
using Ohrbob.Web.Services;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ohrbob.Web.Configuration;
using System.Net.Http;
using System.Threading.Tasks;
using Ohrbob.Server.Shared.Data;
using System.Net;
using Moq.Protected;
using System.Threading;
using System.Text.Json;

namespace Ohrbob.Web.Tests;

public class AccountApiServiceChangelogTests
{
    [Test]
    public async Task UpdateLastSeenChangelogAsync_Sends_Correct_Request()
    {
        var mockLogger = new Mock<ILogger<OhrbobApiService>>();
        var mockOptions = new Mock<IOptions<OhrbobApiOptions>>();
        mockOptions.Setup(o => o.Value).Returns(new OhrbobApiOptions { BaseUrl = "http://test", ApiKey = "testkey" });

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => 
                    r.Method == HttpMethod.Post && 
                    r.RequestUri.ToString() == "http://test/account/id/123/changelog"),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new Account { Id = "123", LastSeenChangelog = "v2.0.0" }))
            })
            .Verifiable();

        var client = new HttpClient(handlerMock.Object);
        var mockClientFactory = new Mock<IHttpClientFactory>();
        mockClientFactory.Setup(cf => cf.CreateClient(It.IsAny<string>())).Returns(client);

        var service = new AccountApiService(mockLogger.Object, mockOptions.Object, mockClientFactory.Object);

        // Act
        var result = await service.UpdateLastSeenChangelogAsync("123", "v2.0.0");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.LastSeenChangelog, Is.EqualTo("v2.0.0"));
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Post),
            ItExpr.IsAny<CancellationToken>()
        );
    }
}
