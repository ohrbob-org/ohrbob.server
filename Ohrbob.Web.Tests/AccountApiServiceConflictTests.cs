using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using Ohrbob.Web.Configuration;
using Ohrbob.Web.Services;

namespace Ohrbob.Web.Tests;

public class AccountApiServiceConflictTests
{
    [Test]
    public async Task CreateOrUpdateAccountAsync_Throws_WhenEmailAlreadyRegistered()
    {
        var mockLogger = new Mock<ILogger<OhrbobApiService>>();
        var mockOptions = new Mock<IOptions<OhrbobApiOptions>>();
        mockOptions.Setup(o => o.Value).Returns(new OhrbobApiOptions { BaseUrl = "http://test", ApiKey = "testkey" });

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.Conflict });

        var client = new HttpClient(handlerMock.Object);
        var mockClientFactory = new Mock<IHttpClientFactory>();
        mockClientFactory.Setup(cf => cf.CreateClient(It.IsAny<string>())).Returns(client);

        var service = new AccountApiService(mockLogger.Object, mockOptions.Object, mockClientFactory.Object);

        Assert.ThrowsAsync<AccountAlreadyRegisteredException>(() =>
            service.CreateOrUpdateAccountAsync("Name", "user@example.com", "", "https://idp", "user-id"));
    }
}
