using Ohrbob.Server.Shared.Requests;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Controllers;
using Ohrbob.Web.Services;
using Ohrbob.Web;

namespace Ohrbob.Web.Tests;

public class StationsControllerTests
{
    private Mock<StationsApiService> _stationsServiceMock;
    private Mock<ProfilesApiService> _profilesServiceMock;
    private StationsController _controller;
    private const string TestAccountId = "test-account-id";

    [SetUp]
    public void Setup()
    {
        var loggerMock = new Mock<Microsoft.Extensions.Logging.ILogger<OhrbobApiService>>();
        var optionsMock = Microsoft.Extensions.Options.Options.Create(new Ohrbob.Web.Configuration.OhrbobApiOptions
        {
            BaseUrl = "https://test.api/",
            ApiKey = "test-api-key"
        });
        var clientFactoryMock = new Mock<IHttpClientFactory>();
        
        _stationsServiceMock = new Mock<StationsApiService>(loggerMock.Object, optionsMock, clientFactoryMock.Object);
        _profilesServiceMock = new Mock<ProfilesApiService>(loggerMock.Object, optionsMock, clientFactoryMock.Object);
        
        _controller = new StationsController(_stationsServiceMock.Object, _profilesServiceMock.Object);

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
        httpContext.Request.ContentType = "application/x-www-form-urlencoded";
        httpContext.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>());

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
    }

    [Test]
    public async Task Index_ReturnsViewWithStationsForCurrentUser()
    {
        var stations = new List<StationWithFeedsDto> { new StationWithFeedsDto { Station = new Station { Name = "Station 1" } } };
        _stationsServiceMock
            .Setup(s => s.GetStationsByAccountIdAsync(TestAccountId))
            .ReturnsAsync(stations);

        var result = await _controller.Index();

        var viewResult = result as ViewResult;
        Assert.That(viewResult, Is.Not.Null);
        var model = viewResult.Model as List<StationWithFeedsDto>;
        Assert.That(model, Is.Not.Null);
        Assert.That(model.Count, Is.EqualTo(1));
        Assert.That(model[0].Station.Name, Is.EqualTo("Station 1"));
    }
    
    [Test]
    public void Create_Get_ReturnsView()
    {
        var result = _controller.Create();
        var viewResult = result as ViewResult;
        Assert.That(viewResult, Is.Not.Null);
    }
    
    [Test]
    public async Task Create_Post_ValidStation_CreatesAndRedirects()
    {
        var newStation = new Station { Name = "New Station", IsPublic = true };
        
        _stationsServiceMock
            .Setup(s => s.CreateStationAsync(TestAccountId, newStation))
            .ReturnsAsync(newStation);

        var result = await _controller.Create(newStation);

        var redirectResult = result as RedirectToActionResult;
        Assert.That(redirectResult, Is.Not.Null);
        Assert.That(redirectResult.ActionName, Is.EqualTo(nameof(StationsController.Index)));
        
        _stationsServiceMock.Verify(s => s.CreateStationAsync(TestAccountId, It.Is<Station>(st => st.IsPublic == true)), Times.Once);
    }
    
    [Test]
    public async Task Create_Post_LimitExceeded_AddsModelErrorAndReturnsView()
    {
        var newStation = new Station { Name = "Limit Station" };
        var exceptionMessage = "Station limit reached for Free tier.";
        _stationsServiceMock
            .Setup(s => s.CreateStationAsync(TestAccountId, newStation))
            .ThrowsAsync(new LimitExceededException(exceptionMessage));

        var result = await _controller.Create(newStation);

        var viewResult = result as ViewResult;
        Assert.That(viewResult, Is.Not.Null);
        Assert.That(viewResult.Model, Is.EqualTo(newStation));
        Assert.That(_controller.ModelState.IsValid, Is.False);
        
        var error = _controller.ModelState[string.Empty]?.Errors.FirstOrDefault();
        Assert.That(error, Is.Not.Null);
        Assert.That(error!.ErrorMessage, Is.EqualTo(exceptionMessage));
    }
    
    [Test]
    public async Task Delete_Post_ValidId_DeletesAndRedirects()
    {
        var stationId = "station-123";
        
        _stationsServiceMock
            .Setup(s => s.GetStationByIdAsync(stationId))
            .ReturnsAsync(new Station { Id = stationId, AccountId = TestAccountId });

        var result = await _controller.Delete(stationId);

        var redirectResult = result as RedirectToActionResult;
        Assert.That(redirectResult, Is.Not.Null);
        Assert.That(redirectResult.ActionName, Is.EqualTo(nameof(StationsController.Index)));
        
        _stationsServiceMock.Verify(s => s.DeleteStationAsync(stationId), Times.Once);
    }

    [Test]
    public async Task Edit_Get_ValidId_ReturnsViewWithStation()
    {
        var stationId = "st-123";
        var station = new Station { Id = stationId, Name = "St To Edit", AccountId = TestAccountId };
        var dto = new Ohrbob.Server.Shared.Requests.StationWithFeedsDto { Station = station };
        
        _stationsServiceMock
            .Setup(s => s.GetStationDetailsByIdAsync(stationId))
            .ReturnsAsync(dto);

        var result = await _controller.Edit(stationId);

        var viewResult = result as ViewResult;
        Assert.That(viewResult, Is.Not.Null);
        var model = viewResult.Model as Ohrbob.Server.Shared.Requests.StationWithFeedsDto;
        Assert.That(model, Is.Not.Null);
        Assert.That(model.Station.Name, Is.EqualTo("St To Edit"));
    }

    [Test]
    public async Task Edit_Post_ValidStation_UpdatesAndRedirects()
    {
        var stationId = "st-123";
        var station = new Station { Id = stationId, Name = "Updated St", AccountId = TestAccountId, IsPublic = true };

        _stationsServiceMock
            .Setup(s => s.GetStationByIdAsync(stationId))
            .ReturnsAsync(new Station { Id = stationId, AccountId = TestAccountId });
        
        _stationsServiceMock
            .Setup(s => s.UpdateStationAsync(station))
            .ReturnsAsync(station);

        var result = await _controller.Edit(stationId, station);

        var redirectResult = result as RedirectToActionResult;
        Assert.That(redirectResult, Is.Not.Null);
        Assert.That(redirectResult.ActionName, Is.EqualTo(nameof(StationsController.Index)));
        
        _stationsServiceMock.Verify(s => s.UpdateStationAsync(It.Is<Station>(st => st.IsPublic == true)), Times.Once);
    }

    [Test]
    public async Task UpdateSchedule_ValidSchedule_UpdatesAndReturnsOk()
    {
        var stationId = "st-123";
        var schedule = new StationSchedule();

        var result = await _controller.UpdateSchedule(stationId, schedule);

        var okResult = result as OkResult;
        Assert.That(okResult, Is.Not.Null);
        
        _stationsServiceMock.Verify(s => s.UpdateScheduleAsync(stationId, schedule), Times.Once);
    }

    [Test]
    public async Task UpdateSettings_ValidParameters_UpdatesAndReturnsOk()
    {
        var stationId = "st-123";
        var existingStation = new Station { Id = stationId, AccountId = TestAccountId, Name = "Old Name", IsAging = false };
        
        _stationsServiceMock
            .Setup(s => s.GetStationByIdAsync(stationId))
            .ReturnsAsync(existingStation);

        _stationsServiceMock
            .Setup(s => s.UpdateStationAsync(existingStation))
            .ReturnsAsync(existingStation);

        var result = await _controller.UpdateSettings(stationId, "New Name", true);

        var okResult = result as OkObjectResult;
        Assert.That(okResult, Is.Not.Null);
        
        var jsonValue = okResult.Value;
        Assert.That(jsonValue, Is.Not.Null);
        
        _stationsServiceMock.Verify(s => s.UpdateStationAsync(It.Is<Station>(s => s.Name == "New Name" && s.IsAging == true)), Times.Once);
    }

    private static Station MakeStation(string id, string accountId, string name, params (string url, FeedSortOrder order, int eps)[] feeds)
    {
        var station = new Station { Id = id, AccountId = accountId, Name = name };
        foreach (var f in feeds)
            station.Schedule.Feeds.Add(new StationFeedSchedule { Url = f.url, SortOrder = f.order, EpisodesToPlay = f.eps });
        station.Feeds = station.Schedule.Feeds.Select(f => f.Url).ToArray();
        return station;
    }

    [Test]
    public async Task MoveFeed_ExistingStation_CarriesSettingsAndRemovesFromSource()
    {
        var url = "https://example.com/feed";
        var source = MakeStation("src", TestAccountId, "Source", (url, FeedSortOrder.Randomized, 5));
        var target = MakeStation("tgt", TestAccountId, "Target");

        _stationsServiceMock.Setup(s => s.GetStationByIdAsync("src")).ReturnsAsync(source);
        _stationsServiceMock.Setup(s => s.GetStationByIdAsync("tgt")).ReturnsAsync(target);
        _stationsServiceMock.Setup(s => s.UpdateStationAsync(It.IsAny<Station>())).ReturnsAsync((Station st) => st);

        var result = await _controller.MoveFeed(new MoveFeedRequest
        {
            SourceStationId = "src", FeedUrl = url, FeedTitle = "Feed", TargetStationId = "tgt"
        });

        Assert.That(result, Is.InstanceOf<StatusCodeResult>());
        Assert.That(((StatusCodeResult)result).StatusCode, Is.EqualTo(204));
        _stationsServiceMock.Verify(s => s.UpdateStationAsync(It.Is<Station>(st =>
            st.Id == "tgt" && st.Schedule.Feeds.Any(f => f.Url == url && f.SortOrder == FeedSortOrder.Randomized && f.EpisodesToPlay == 5))), Times.Once);
        _stationsServiceMock.Verify(s => s.UpdateStationAsync(It.Is<Station>(st =>
            st.Id == "src" && !st.Schedule.Feeds.Any(f => f.Url == url) && !st.Feeds.Contains(url))), Times.Once);
        Assert.That(_controller.Response.Headers["HX-Trigger"].ToString(), Does.Contain("showToast"));
    }

    [Test]
    public async Task MoveFeed_CreateNewStation_NamesFromTitle_WhenNameBlank()
    {
        var url = "https://example.com/feed";
        var source = MakeStation("src", TestAccountId, "Source", (url, FeedSortOrder.OldestToNewest, 3));

        _stationsServiceMock.Setup(s => s.GetStationByIdAsync("src")).ReturnsAsync(source);
        _stationsServiceMock.Setup(s => s.CreateStationAsync(TestAccountId, It.IsAny<Station>()))
            .ReturnsAsync((string acct, Station st) => { st.Id = "new-station"; return st; });
        _stationsServiceMock.Setup(s => s.UpdateStationAsync(It.IsAny<Station>())).ReturnsAsync((Station st) => st);
        _controller.ControllerContext.HttpContext.Request.Headers["Cookie"] = "Ohrbob_ActiveProfileId=prof-1";

        var result = await _controller.MoveFeed(new MoveFeedRequest
        {
            SourceStationId = "src", FeedUrl = url, FeedTitle = "My Feed", CreateNewStation = true
        });

        Assert.That(result, Is.InstanceOf<StatusCodeResult>());
        Assert.That(((StatusCodeResult)result).StatusCode, Is.EqualTo(204));
        _stationsServiceMock.Verify(s => s.CreateStationAsync(TestAccountId, It.Is<Station>(st =>
            st.Name == "My Feed" && st.Schedule.Feeds.Any(f => f.Url == url && f.EpisodesToPlay == 3))), Times.Once);
        _profilesServiceMock.Verify(p => p.AddStationToProfileAsync("prof-1", "new-station"), Times.Once);
    }

    [Test]
    public async Task MoveFeed_NotOwner_ReturnsNotFound()
    {
        _stationsServiceMock.Setup(s => s.GetStationByIdAsync("src"))
            .ReturnsAsync(MakeStation("src", "someone-else", "Foreign", ("https://e/f", FeedSortOrder.OldestToNewest, 1)));

        var result = await _controller.MoveFeed(new MoveFeedRequest
        { SourceStationId = "src", FeedUrl = "https://e/f", FeedTitle = "F", TargetStationId = "tgt" });

        Assert.That(result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public async Task MoveFeed_SourceEqualsTarget_Returns422()
    {
        _stationsServiceMock.Setup(s => s.GetStationByIdAsync("src"))
            .ReturnsAsync(MakeStation("src", TestAccountId, "Source", ("https://e/f", FeedSortOrder.OldestToNewest, 1)));

        var result = await _controller.MoveFeed(new MoveFeedRequest
        { SourceStationId = "src", FeedUrl = "https://e/f", FeedTitle = "F", TargetStationId = "src" });

        Assert.That(result, Is.InstanceOf<ObjectResult>());
        Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(422));
    }

    [Test]
    public async Task MoveFeed_FeedNotInSource_Returns422()
    {
        _stationsServiceMock.Setup(s => s.GetStationByIdAsync("src"))
            .ReturnsAsync(MakeStation("src", TestAccountId, "Source"));

        var result = await _controller.MoveFeed(new MoveFeedRequest
        { SourceStationId = "src", FeedUrl = "https://e/f", FeedTitle = "F", TargetStationId = "tgt" });

        Assert.That(result, Is.InstanceOf<ObjectResult>());
        Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(422));
    }

    [Test]
    public async Task MoveFeed_TargetAlreadyHasFeed_Returns422()
    {
        var url = "https://e/f";
        _stationsServiceMock.Setup(s => s.GetStationByIdAsync("src"))
            .ReturnsAsync(MakeStation("src", TestAccountId, "Source", (url, FeedSortOrder.OldestToNewest, 1)));
        _stationsServiceMock.Setup(s => s.GetStationByIdAsync("tgt"))
            .ReturnsAsync(MakeStation("tgt", TestAccountId, "Target", (url, FeedSortOrder.OldestToNewest, 1)));

        var result = await _controller.MoveFeed(new MoveFeedRequest
        { SourceStationId = "src", FeedUrl = url, FeedTitle = "F", TargetStationId = "tgt" });

        Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(422));
        _stationsServiceMock.Verify(s => s.UpdateStationAsync(It.IsAny<Station>()), Times.Never);
    }

    [Test]
    public async Task MoveFeed_DestinationLimitExceeded_LeavesSourceUnchanged()
    {
        var url = "https://e/f";
        _stationsServiceMock.Setup(s => s.GetStationByIdAsync("src"))
            .ReturnsAsync(MakeStation("src", TestAccountId, "Source", (url, FeedSortOrder.OldestToNewest, 1)));
        _stationsServiceMock.Setup(s => s.GetStationByIdAsync("tgt"))
            .ReturnsAsync(MakeStation("tgt", TestAccountId, "Target"));
        _stationsServiceMock.Setup(s => s.UpdateStationAsync(It.Is<Station>(st => st.Id == "tgt")))
            .ThrowsAsync(new LimitExceededException("Feed limit reached."));

        var result = await _controller.MoveFeed(new MoveFeedRequest
        { SourceStationId = "src", FeedUrl = url, FeedTitle = "F", TargetStationId = "tgt" });

        Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(422));
        _stationsServiceMock.Verify(s => s.UpdateStationAsync(It.Is<Station>(st => st.Id == "src")), Times.Never);
    }

    [Test]
    public async Task MoveFeed_SourceRemovalFails_LeavesFeedInBoth()
    {
        var url = "https://e/f";
        _stationsServiceMock.Setup(s => s.GetStationByIdAsync("src"))
            .ReturnsAsync(MakeStation("src", TestAccountId, "Source", (url, FeedSortOrder.OldestToNewest, 1)));
        _stationsServiceMock.Setup(s => s.GetStationByIdAsync("tgt"))
            .ReturnsAsync(MakeStation("tgt", TestAccountId, "Target"));
        _stationsServiceMock.Setup(s => s.UpdateStationAsync(It.Is<Station>(st => st.Id == "tgt")))
            .ReturnsAsync((Station st) => st);
        _stationsServiceMock.Setup(s => s.UpdateStationAsync(It.Is<Station>(st => st.Id == "src")))
            .ThrowsAsync(new System.Net.Http.HttpRequestException("boom"));

        var result = await _controller.MoveFeed(new MoveFeedRequest
        { SourceStationId = "src", FeedUrl = url, FeedTitle = "F", TargetStationId = "tgt" });

        Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(422));
        Assert.That(_controller.Response.Headers["HX-Trigger"].ToString(), Does.Contain("showToast"));
        _stationsServiceMock.Verify(s => s.UpdateStationAsync(It.Is<Station>(st => st.Id == "tgt")), Times.Once);
    }

    [Test]
    public async Task MoveFeed_CarriesFeedUrlVerbatim()
    {
        var url = "https://example.com/feed?a=1&b=2";
        _stationsServiceMock.Setup(s => s.GetStationByIdAsync("src"))
            .ReturnsAsync(MakeStation("src", TestAccountId, "Source", (url, FeedSortOrder.OldestToNewest, 1)));
        _stationsServiceMock.Setup(s => s.GetStationByIdAsync("tgt"))
            .ReturnsAsync(MakeStation("tgt", TestAccountId, "Target"));
        _stationsServiceMock.Setup(s => s.UpdateStationAsync(It.IsAny<Station>())).ReturnsAsync((Station st) => st);

        var result = await _controller.MoveFeed(new MoveFeedRequest
        { SourceStationId = "src", FeedUrl = url, FeedTitle = "F", TargetStationId = "tgt" });

        Assert.That(((StatusCodeResult)result).StatusCode, Is.EqualTo(204));
        _stationsServiceMock.Verify(s => s.UpdateStationAsync(It.Is<Station>(st =>
            st.Id == "tgt" && st.Schedule.Feeds.Any(f => f.Url == url))), Times.Once);
    }

    [TearDown]
    public void TearDown()
    {
        _controller?.Dispose();
    }
}
