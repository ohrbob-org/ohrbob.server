using Ohrbob.Server.Shared.Requests;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Controllers;
using Ohrbob.Web.Services;
using Ohrbob.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Ohrbob.Web.Tests;

public class DiscoverControllerTests
{
    private Mock<StationsApiService> _stationsServiceMock;
    private Mock<ProfilesApiService> _profilesServiceMock;
    private DiscoverController _controller;
    private const string TestAccountId = "test-account-id";

    [SetUp]
    public void Setup()
    {
        var loggerMock = new Mock<ILogger<OhrbobApiService>>();
        var optionsMock = Microsoft.Extensions.Options.Options.Create(new Ohrbob.Web.Configuration.OhrbobApiOptions
        {
            BaseUrl = "https://test.api/",
            ApiKey = "test-api-key"
        });
        var clientFactoryMock = new Mock<IHttpClientFactory>();

        _stationsServiceMock = new Mock<StationsApiService>(loggerMock.Object, optionsMock, clientFactoryMock.Object);
        _profilesServiceMock = new Mock<ProfilesApiService>(loggerMock.Object, optionsMock, clientFactoryMock.Object);

        var configMock = new Mock<IConfiguration>();
        var podcastLoggerMock = new Mock<ILogger<PodcastIndexApiService>>();
        var podcastIndexApi = new PodcastIndexApiService(new HttpClient(), configMock.Object, podcastLoggerMock.Object);

        _controller = new DiscoverController(podcastIndexApi, _stationsServiceMock.Object, _profilesServiceMock.Object);

        // Setup mock user context with the Consts.OhrbobAccountIdClaim
        var claims = new List<Claim>
        {
            new Claim(Consts.OhrbobAccountIdClaim, TestAccountId)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var claimsPrincipal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext
        {
            User = claimsPrincipal
        };

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
    }

    [Test]
    public async Task AddFeed_MissingAccountId_ReturnsUnauthorized()
    {
        // Arrange
        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());

        // Act
        var result = await _controller.AddFeed(new AddFeedRequest());

        // Assert
        Assert.That(result, Is.InstanceOf<UnauthorizedResult>());
    }

    [Test]
    public async Task AddFeed_InvalidFeedData_Returns422()
    {
        // Arrange
        var request = new AddFeedRequest
        {
            FeedUrl = "",
            FeedTitle = "Some Title"
        };

        // Act
        var result = await _controller.AddFeed(request);

        // Assert
        Assert.That(result, Is.InstanceOf<ObjectResult>());
        var objResult = result as ObjectResult;
        Assert.That(objResult.StatusCode, Is.EqualTo(422));
        Assert.That(objResult.Value, Is.EqualTo("Invalid feed data."));
    }

    [Test]
    public async Task AddFeed_NoStationsSelected_Returns422()
    {
        // Arrange
        var request = new AddFeedRequest
        {
            FeedUrl = "https://test.feed",
            FeedTitle = "Some Title",
            CreateNewStation = false,
            StationIds = new List<string>()
        };

        // Act
        var result = await _controller.AddFeed(request);

        // Assert
        Assert.That(result, Is.InstanceOf<ObjectResult>());
        var objResult = result as ObjectResult;
        Assert.That(objResult.StatusCode, Is.EqualTo(422));
        Assert.That(objResult.Value, Is.EqualTo("No stations selected."));
    }

    [Test]
    public async Task AddFeed_CreateNewStation_CreatesStationAndAddsToProfile()
    {
        // Arrange
        var request = new AddFeedRequest
        {
            FeedUrl = "https://test.feed",
            FeedTitle = "Some Title",
            CreateNewStation = true,
            NewStationName = "My New Station"
        };

        var createdStation = new Station { Id = "new-station-123", Name = "My New Station" };
        _stationsServiceMock
            .Setup(s => s.CreateStationAsync(TestAccountId, It.IsAny<Station>()))
            .ReturnsAsync(createdStation);

        _profilesServiceMock
            .Setup(p => p.GetProfilesByAccountIdAsync(TestAccountId))
            .ReturnsAsync(new List<Profile> { new Profile { Id = "profile-123" } });

        // Act
        var result = await _controller.AddFeed(request);

        // Assert
        Assert.That(result, Is.InstanceOf<StatusCodeResult>());
        var statusResult = result as StatusCodeResult;
        Assert.That(statusResult.StatusCode, Is.EqualTo(204));

        _stationsServiceMock.Verify(s => s.CreateStationAsync(TestAccountId, It.Is<Station>(st => st.Name == "My New Station")), Times.Once);
        _profilesServiceMock.Verify(p => p.AddStationToProfileAsync("profile-123", "new-station-123"), Times.Once);

        var hxTrigger = _controller.Response.Headers["HX-Trigger"].ToString();
        Assert.That(hxTrigger, Does.Contain("Created new station: My New Station"));
    }

    [Test]
    public async Task AddFeed_AddToExistingStations_UpdatesStationsWithFeed()
    {
        // Arrange
        var request = new AddFeedRequest
        {
            FeedUrl = "https://test.feed",
            FeedTitle = "Some Title",
            CreateNewStation = false,
            StationIds = new List<string> { "station-1", "station-2" }
        };

        var station1 = new Station { Id = "station-1", Name = "Station One", AccountId = TestAccountId, Feeds = new[] { "https://existing.feed" } };
        var station2 = new Station { Id = "station-2", Name = "Station Two", AccountId = TestAccountId };

        _stationsServiceMock.Setup(s => s.GetStationByIdAsync("station-1")).ReturnsAsync(station1);
        _stationsServiceMock.Setup(s => s.GetStationByIdAsync("station-2")).ReturnsAsync(station2);

        // Act
        var result = await _controller.AddFeed(request);

        // Assert
        Assert.That(result, Is.InstanceOf<StatusCodeResult>());
        var statusResult = result as StatusCodeResult;
        Assert.That(statusResult.StatusCode, Is.EqualTo(204));

        _stationsServiceMock.Verify(s => s.UpdateStationAsync(It.Is<Station>(st => st.Id == "station-1")), Times.Once);
        _stationsServiceMock.Verify(s => s.UpdateStationAsync(It.Is<Station>(st => st.Id == "station-2")), Times.Once);

        var hxTrigger = _controller.Response.Headers["HX-Trigger"].ToString();
        Assert.That(hxTrigger, Does.Contain("Added podcast to: Station One, Station Two"));
    }

    [Test]
    public async Task AddFeed_LimitExceededException_Returns422WithToast()
    {
        // Arrange
        var request = new AddFeedRequest
        {
            FeedUrl = "https://test.feed",
            FeedTitle = "Some Title",
            CreateNewStation = true
        };

        _stationsServiceMock
            .Setup(s => s.CreateStationAsync(TestAccountId, It.IsAny<Station>()))
            .ThrowsAsync(new LimitExceededException("Limit exceeded!"));

        // Act
        var result = await _controller.AddFeed(request);

        // Assert
        Assert.That(result, Is.InstanceOf<ObjectResult>());
        var objResult = result as ObjectResult;
        Assert.That(objResult.StatusCode, Is.EqualTo(422));
        Assert.That(objResult.Value, Is.EqualTo("Limit exceeded!"));

        var hxTrigger = _controller.Response.Headers["HX-Trigger"].ToString();
        Assert.That(hxTrigger, Does.Contain("Limit exceeded!"));
        Assert.That(hxTrigger, Does.Contain("danger"));
    }

    [TearDown]
    public void TearDown()
    {
        _controller?.Dispose();
    }
}