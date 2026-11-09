using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Server.Shared.Requests;
using Ohrbob.Web;
using Ohrbob.Web.Configuration;
using Ohrbob.Web.Controllers;
using Ohrbob.Web.Models;
using Ohrbob.Web.Services;

namespace Ohrbob.Web.Tests;

public class StreamControllerTests
{
    private const string TestAccountId = "test-account-id";

    private Mock<StationsApiService> _stationsServiceMock;
    private Mock<ProfilesApiService> _profilesServiceMock;
    private Mock<AccountApiService> _accountsServiceMock;
    private StreamController _controller;

    [SetUp]
    public void Setup()
    {
        var loggerMock = new Mock<Microsoft.Extensions.Logging.ILogger<OhrbobApiService>>();
        var optionsMock = Microsoft.Extensions.Options.Options.Create(new OhrbobApiOptions
        {
            BaseUrl = "https://test.api/",
            ApiKey = "test-api-key"
        });
        var clientFactoryMock = new Mock<IHttpClientFactory>();

        _stationsServiceMock = new Mock<StationsApiService>(loggerMock.Object, optionsMock, clientFactoryMock.Object);
        _profilesServiceMock = new Mock<ProfilesApiService>(loggerMock.Object, optionsMock, clientFactoryMock.Object);
        _accountsServiceMock = new Mock<AccountApiService>(loggerMock.Object, optionsMock, clientFactoryMock.Object);
        _accountsServiceMock
            .Setup(s => s.GetLimitsAsync(TestAccountId))
            .ReturnsAsync(new TierLimits { StreamEnabled = true });

        _controller = new StreamController(
            _profilesServiceMock.Object,
            _stationsServiceMock.Object,
            _accountsServiceMock.Object,
            Options.Create(new StreamOptions { PublicBaseUrl = "https://stream.test/" }));

        var claims = new List<Claim> { new(Consts.OhrbobAccountIdClaim, TestAccountId) };
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
            }
        };
    }

    [Test]
    public async Task Index_GroupsStationsByProfileAndSkipsUnknownStations()
    {
        _profilesServiceMock
            .Setup(s => s.GetProfilesByAccountIdAsync(TestAccountId))
            .ReturnsAsync(new List<Profile>
            {
                new() { Name = "Alice", StreamToken = "tok-a", AllowedStationIds = new[] { "s1", "missing" } },
                new() { Name = "Bob", StreamToken = "tok-b", AllowedStationIds = new[] { "s2" } }
            });
        _stationsServiceMock
            .Setup(s => s.GetStationsByAccountIdAsync(TestAccountId))
            .ReturnsAsync(new List<StationWithFeedsDto>
            {
                new() { Station = new Station { Id = "s1", Name = "Station One" } },
                new() { Station = new Station { Id = "s2", Name = "Station Two" } }
            });

        var result = await _controller.Index();

        var model = (result as ViewResult)?.Model as StreamViewModel;
        Assert.That(model, Is.Not.Null);
        Assert.That(model!.Profiles.Select(p => p.ProfileName), Is.EqualTo(new[] { "Alice", "Bob" }));
        Assert.That(model.StreamEnabled, Is.True);
        Assert.That(model.Profiles[0].Stations, Has.Count.EqualTo(1));
        Assert.That(model.Profiles[0].Stations[0].StationName, Is.EqualTo("Station One"));
        Assert.That(model.Profiles[0].Stations[0].Url,
            Is.EqualTo($"https://stream.test/{TestAccountId}/s1?key=tok-a"));
        Assert.That(model.Profiles[1].Stations[0].Url,
            Is.EqualTo($"https://stream.test/{TestAccountId}/s2?key=tok-b"));
    }

    [Test]
    public async Task Index_WithoutStreamingPlan_ReportsStreamDisabled()
    {
        _profilesServiceMock
            .Setup(s => s.GetProfilesByAccountIdAsync(TestAccountId))
            .ReturnsAsync(new List<Profile>());
        _stationsServiceMock
            .Setup(s => s.GetStationsByAccountIdAsync(TestAccountId))
            .ReturnsAsync(new List<StationWithFeedsDto>());
        _accountsServiceMock
            .Setup(s => s.GetLimitsAsync(TestAccountId))
            .ReturnsAsync(new TierLimits { StreamEnabled = false });

        var result = await _controller.Index();

        var model = (result as ViewResult)?.Model as StreamViewModel;
        Assert.That(model, Is.Not.Null);
        Assert.That(model!.StreamEnabled, Is.False);
    }

    [Test]
    public async Task Index_WithoutAccountClaim_ReturnsForbid()
    {
        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await _controller.Index();

        Assert.That(result, Is.InstanceOf<ForbidResult>());
    }

    [TearDown]
    public void TearDown()
    {
        _controller?.Dispose();
    }
}
