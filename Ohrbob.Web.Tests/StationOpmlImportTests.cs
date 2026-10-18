using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Server.Shared.Requests;
using Ohrbob.Web;
using Ohrbob.Web.Controllers;
using Ohrbob.Web.Services;

namespace Ohrbob.Web.Tests;

public class StationOpmlImportTests
{
    private Mock<StationsApiService> _stationsServiceMock;
    private Mock<ProfilesApiService> _profilesServiceMock;
    private StationsController _controller;
    private const string TestAccountId = "test-account-id";
    private const string StationId = "station-1";

    private const string SampleOpml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<opml version=""2.0"">
  <body>
    <outline text=""A"" type=""rss"" xmlUrl=""https://a.example/feed"" />
    <outline text=""B"" type=""rss"" xmlUrl=""https://b.example/feed"" />
  </body>
</opml>";

    [SetUp]
    public void Setup()
    {
        _controller = CreateController(TestAccountId);
        _stationsServiceMock.Setup(s => s.GetStationByIdAsync(StationId))
            .ReturnsAsync(new Station { Id = StationId, AccountId = TestAccountId });
    }

    [TearDown]
    public void TearDown()
    {
        _controller?.Dispose();
    }

    private StationsController CreateController(string? accountId)    {
        var loggerMock = new Mock<Microsoft.Extensions.Logging.ILogger<OhrbobApiService>>();
        var optionsMock = Microsoft.Extensions.Options.Options.Create(new Ohrbob.Web.Configuration.OhrbobApiOptions
        {
            BaseUrl = "https://test.api/",
            ApiKey = "test-api-key"
        });
        var clientFactoryMock = new Mock<IHttpClientFactory>();

        _stationsServiceMock = new Mock<StationsApiService>(loggerMock.Object, optionsMock, clientFactoryMock.Object);
        _profilesServiceMock = new Mock<ProfilesApiService>(loggerMock.Object, optionsMock, clientFactoryMock.Object);

        var controller = new StationsController(_stationsServiceMock.Object, _profilesServiceMock.Object);

        var claims = new List<Claim>();
        if (accountId != null)
            claims.Add(new Claim(Consts.OhrbobAccountIdClaim, accountId));
        var identity = new ClaimsIdentity(claims, "TestAuth");

        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        httpContext.Request.ContentType = "multipart/form-data";

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private static IFormFile OpmlFile(string content, string fileName = "subscriptions.opml")
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "opml", fileName);
    }

    private static string? ToastMessage(StationsController controller)
    {
        var header = controller.Response.Headers["HX-Trigger"].ToString();
        return string.IsNullOrEmpty(header) ? null : header;
    }

    [Test]
    public async Task ImportOpml_WithoutAccount_ReturnsForbid()
    {
        _controller = CreateController(null);

        var result = await _controller.ImportOpml(StationId, OpmlFile(SampleOpml));

        Assert.That(result, Is.InstanceOf<ForbidResult>());
    }

    [Test]
    public async Task ImportOpml_StationNotOwnedByAccount_ReturnsNotFound()
    {
        _stationsServiceMock.Setup(s => s.GetStationByIdAsync(StationId))
            .ReturnsAsync(new Station { Id = StationId, AccountId = "someone-else" });

        var result = await _controller.ImportOpml(StationId, OpmlFile(SampleOpml));

        Assert.That(result, Is.InstanceOf<NotFoundResult>());
        _stationsServiceMock.Verify(s => s.AddFeedsAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Test]
    public async Task ImportOpml_MissingFile_ReturnsToastError()
    {
        var result = await _controller.ImportOpml(StationId, null!);

        Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(422));
        Assert.That(ToastMessage(_controller), Does.Contain("showToast"));
        _stationsServiceMock.Verify(s => s.AddFeedsAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Test]
    public async Task ImportOpml_NotAnOpmlDocument_ReturnsToastError()
    {
        var result = await _controller.ImportOpml(StationId, OpmlFile("<html><body>nope</body></html>"));

        Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(422));
        Assert.That(ToastMessage(_controller), Does.Contain("showToast"));
        _stationsServiceMock.Verify(s => s.AddFeedsAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Test]
    public async Task ImportOpml_OpmlWithoutFeeds_ReturnsToastError()
    {
        var result = await _controller.ImportOpml(StationId, OpmlFile("<opml version=\"2.0\"><body></body></opml>"));

        Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(422));
        Assert.That(ToastMessage(_controller), Does.Contain("showToast"));
        _stationsServiceMock.Verify(s => s.AddFeedsAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Test]
    public async Task ImportOpml_PassesParsedUrlsToApi_AndReportsResult()
    {
        _stationsServiceMock
            .Setup(s => s.AddFeedsAsync(StationId, It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new FeedImportResultDto
            {
                Added = { "https://b.example/feed" },
                AlreadyPresent = { "https://a.example/feed" }
            });

        var result = await _controller.ImportOpml(StationId, OpmlFile(SampleOpml));

        Assert.That(result, Is.InstanceOf<StatusCodeResult>());
        Assert.That(((StatusCodeResult)result).StatusCode, Is.EqualTo(204));

        _stationsServiceMock.Verify(s => s.AddFeedsAsync(StationId,
            It.Is<IEnumerable<string>>(u => u.SequenceEqual(new[]
            {
                "https://a.example/feed",
                "https://b.example/feed"
            }))), Times.Once);

        var toast = ToastMessage(_controller);
        Assert.That(toast, Does.Contain("showToast"));
        Assert.That(toast, Does.Contain("Imported 1"));
    }

    [Test]
    public async Task ImportOpml_WhenLimitReached_ReportsSkippedFeeds()
    {
        _stationsServiceMock
            .Setup(s => s.AddFeedsAsync(StationId, It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new FeedImportResultDto
            {
                Added = { "https://a.example/feed" },
                NotAddedDueToLimit = { "https://b.example/feed" }
            });

        var result = await _controller.ImportOpml(StationId, OpmlFile(SampleOpml));

        Assert.That(((StatusCodeResult)result).StatusCode, Is.EqualTo(204));
        var toast = ToastMessage(_controller);
        Assert.That(toast, Does.Contain("Imported 1"));
        Assert.That(toast, Does.Contain("1"));
        Assert.That(toast, Does.Contain("limit"));
    }
}
