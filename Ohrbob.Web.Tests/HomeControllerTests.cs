using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Server.Shared.Requests;
using Ohrbob.Web;
using Ohrbob.Web.Configuration;
using Ohrbob.Web.Controllers;
using Ohrbob.Web.Services;

namespace Ohrbob.Web.Tests;

public class HomeControllerTests
{
    private const string TestAccountId = "test-account-id";
    private Mock<StationsApiService> _stations = null!;
    private Mock<ProfilesApiService> _profiles = null!;

    [SetUp]
    public void Setup()
    {
        var logger = new Mock<ILogger<OhrbobApiService>>();
        var opts = Options.Create(new OhrbobApiOptions { BaseUrl = "https://test.api/", ApiKey = "k" });
        var factory = new Mock<IHttpClientFactory>();
        _stations = new Mock<StationsApiService>(logger.Object, opts, factory.Object);
        _profiles = new Mock<ProfilesApiService>(logger.Object, opts, factory.Object);
    }

    private HomeController CreateController()
    {
        var controller = new HomeController(
            _stations.Object,
            _profiles.Object,
            Options.Create(new LegalOptions()),
            new BackendVersionCache(),
            Options.Create(new AppVersionOptions()));

        var claims = new List<Claim> { new(Consts.OhrbobAccountIdClaim, TestAccountId) };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
            }
        };
        controller.ControllerContext.HttpContext.Request.Headers.Cookie = "Ohrbob_ActiveProfileId=p1";
        return controller;
    }

    [Test]
    public async Task Index_MergesSubscribedStationsIntoTheActiveProfileGrid()
    {
        _profiles.Setup(p => p.GetProfilesByAccountIdAsync(TestAccountId)).ReturnsAsync(new List<Profile>
        {
            new()
            {
                Id = "p1", AccountId = TestAccountId, Name = "Main",
                AllowedStationIds = new[] { "owned" },
                SubscribedStationIds = new[] { "sub-1" }
            }
        });
        _stations.Setup(s => s.GetStationsByAccountIdAsync(TestAccountId, "p1")).ReturnsAsync(new List<StationWithFeedsDto>
        {
            new() { Station = new Station { Id = "owned", Name = "Owned" } }
        });
        _stations.Setup(s => s.GetStationDetailsByIdAsync("sub-1", "p1")).ReturnsAsync(
            new StationWithFeedsDto { Station = new Station { Id = "sub-1", Name = "Shared" }, UnplayedEpisodeCount = 2 });

        var result = await CreateController().Index();

        var model = (result as ViewResult)?.Model as List<StationWithFeedsDto>;
        Assert.That(model!.Select(s => s.Station.Id), Is.EquivalentTo(new[] { "owned", "sub-1" }));
        var subscribed = (result as ViewResult)?.ViewData["SubscribedStationIds"] as HashSet<string>;
        Assert.That(subscribed, Does.Contain("sub-1"));
    }

    [Test]
    public async Task Index_SkipsDeletedSubscribedStations()
    {
        _profiles.Setup(p => p.GetProfilesByAccountIdAsync(TestAccountId)).ReturnsAsync(new List<Profile>
        {
            new()
            {
                Id = "p1", AccountId = TestAccountId, Name = "Main",
                AllowedStationIds = new[] { "owned" },
                SubscribedStationIds = new[] { "gone" }
            }
        });
        _stations.Setup(s => s.GetStationsByAccountIdAsync(TestAccountId, "p1")).ReturnsAsync(new List<StationWithFeedsDto>
        {
            new() { Station = new Station { Id = "owned", Name = "Owned" } }
        });
        _stations.Setup(s => s.GetStationDetailsByIdAsync("gone", "p1")).ReturnsAsync((StationWithFeedsDto?)null);

        var result = await CreateController().Index();

        var model = (result as ViewResult)?.Model as List<StationWithFeedsDto>;
        Assert.That(model!.Select(s => s.Station.Id), Is.EquivalentTo(new[] { "owned" }));
    }
}
