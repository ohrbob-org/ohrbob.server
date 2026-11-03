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
using Ohrbob.Web.Models;
using Ohrbob.Web.Services;

namespace Ohrbob.Web.Tests;

public class ShareControllerTests
{
    private const string TestAccountId = "test-account-id";
    private Mock<StationsApiService> _stations = null!;
    private Mock<ProfilesApiService> _profiles = null!;
    private Mock<AccountApiService> _accounts = null!;

    [SetUp]
    public void Setup()
    {
        var logger = new Mock<ILogger<OhrbobApiService>>();
        var opts = Options.Create(new OhrbobApiOptions { BaseUrl = "https://test.api/", ApiKey = "k" });
        var factory = new Mock<IHttpClientFactory>();
        _stations = new Mock<StationsApiService>(logger.Object, opts, factory.Object);
        _profiles = new Mock<ProfilesApiService>(logger.Object, opts, factory.Object);
        _accounts = new Mock<AccountApiService>(logger.Object, opts, factory.Object);
    }

    private ShareController CreateController(string? accountId = null)
    {
        var controller = new ShareController(_stations.Object, _profiles.Object, _accounts.Object);
        var claims = accountId == null
            ? new List<Claim>()
            : new List<Claim> { new(Consts.OhrbobAccountIdClaim, accountId) };
        var identity = new ClaimsIdentity(claims, accountId == null ? "Anonymous" : "TestAuth");

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };

        return controller;
    }

    [Test]
    public async Task Index_PublicStation_ReturnsViewWithStation()
    {
        var dto = new StationWithFeedsDto
        {
            Station = new Station { Id = "st-1", Name = "Shared Station", IsPublic = true, ShareCode = "Abc123XYZ9" }
        };
        _stations.Setup(s => s.GetStationByShareCodeAsync("Abc123XYZ9")).ReturnsAsync(dto);

        var result = await CreateController().Index("Abc123XYZ9");

        var view = result as ViewResult;
        var model = view?.Model as ShareViewModel;
        Assert.That(model, Is.Not.Null);
        Assert.That(model!.Station.Station.Name, Is.EqualTo("Shared Station"));
        Assert.That(model.IsAuthenticated, Is.False);
    }

    [Test]
    public async Task Index_UnknownOrPrivateStation_ReturnsNotFound()
    {
        _stations.Setup(s => s.GetStationByShareCodeAsync("missing")).ReturnsAsync((StationWithFeedsDto?)null);

        var result = await CreateController().Index("missing");

        Assert.That(result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public async Task Index_AuthenticatedUser_ListsProfilesWithSubscriptionState()
    {
        var dto = new StationWithFeedsDto
        {
            Station = new Station { Id = "st-1", Name = "Shared", IsPublic = true, AccountId = "owner" }
        };
        _stations.Setup(s => s.GetStationByShareCodeAsync("code")).ReturnsAsync(dto);
        _profiles.Setup(p => p.GetProfilesByAccountIdAsync(TestAccountId)).ReturnsAsync(new List<Profile>
        {
            new() { Id = "p1", Name = "Alice", SubscribedStationIds = Array.Empty<string>() },
            new() { Id = "p2", Name = "Bob", SubscribedStationIds = new[] { "st-1" } }
        });

        var controller = CreateController(TestAccountId);
        controller.ControllerContext.HttpContext.Request.Headers.Cookie = "Ohrbob_ActiveProfileId=p1";

        var result = await controller.Index("code");

        var model = (result as ViewResult)?.Model as ShareViewModel;
        Assert.That(model, Is.Not.Null);
        Assert.That(model!.Profiles.Select(p => p.Name), Is.EqualTo(new[] { "Alice", "Bob" }));
        Assert.That(model.Profiles[0].IsActive, Is.True);
        Assert.That(model.Profiles[0].IsSubscribed, Is.False);
        Assert.That(model.Profiles[1].IsSubscribed, Is.True);
    }

    [Test]
    public async Task Index_Owner_IsMarkedAsOwner()
    {
        var dto = new StationWithFeedsDto
        {
            Station = new Station { Id = "st-1", Name = "Mine", IsPublic = true, AccountId = TestAccountId }
        };
        _stations.Setup(s => s.GetStationByShareCodeAsync("code")).ReturnsAsync(dto);
        _profiles.Setup(p => p.GetProfilesByAccountIdAsync(TestAccountId)).ReturnsAsync(new List<Profile>
        {
            new() { Id = "p1", Name = "Alice" }
        });

        var result = await CreateController(TestAccountId).Index("code");

        var model = (result as ViewResult)?.Model as ShareViewModel;
        Assert.That(model!.IsOwner, Is.True);
    }
}
