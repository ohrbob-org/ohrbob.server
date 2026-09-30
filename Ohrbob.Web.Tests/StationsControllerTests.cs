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
        
        _controller = new StationsController(_stationsServiceMock.Object);

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
    public async Task Index_ReturnsViewWithStationsForCurrentUser()
    {
        var stations = new List<Station> { new Station { Name = "Station 1" } };
        _stationsServiceMock
            .Setup(s => s.GetStationsByAccountIdAsync(TestAccountId))
            .ReturnsAsync(stations);

        var result = await _controller.Index();

        var viewResult = result as ViewResult;
        Assert.That(viewResult, Is.Not.Null);
        var model = viewResult.Model as List<Station>;
        Assert.That(model, Is.Not.Null);
        Assert.That(model.Count, Is.EqualTo(1));
        Assert.That(model[0].Name, Is.EqualTo("Station 1"));
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
        var newStation = new Station { Name = "New Station" };
        
        _stationsServiceMock
            .Setup(s => s.CreateStationAsync(TestAccountId, newStation))
            .ReturnsAsync(newStation);

        var result = await _controller.Create(newStation);

        var redirectResult = result as RedirectToActionResult;
        Assert.That(redirectResult, Is.Not.Null);
        Assert.That(redirectResult.ActionName, Is.EqualTo(nameof(StationsController.Index)));
        
        _stationsServiceMock.Verify(s => s.CreateStationAsync(TestAccountId, newStation), Times.Once);
    }
    
    [Test]
    public async Task Delete_Post_ValidId_DeletesAndRedirects()
    {
        var stationId = "station-123";

        var result = await _controller.Delete(stationId);

        var redirectResult = result as RedirectToActionResult;
        Assert.That(redirectResult, Is.Not.Null);
        Assert.That(redirectResult.ActionName, Is.EqualTo(nameof(StationsController.Index)));
        
        _stationsServiceMock.Verify(s => s.DeleteStationAsync(stationId), Times.Once);
    }

    [TearDown]
    public void TearDown()
    {
        _controller?.Dispose();
    }
}
