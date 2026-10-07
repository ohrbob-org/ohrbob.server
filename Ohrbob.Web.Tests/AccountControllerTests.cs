using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Controllers;
using Ohrbob.Web.Services;
using Ohrbob.Web;

namespace Ohrbob.Web.Tests;

public class AccountControllerTests
{
    [Test]
    public async Task Index_ReturnsViewWithTierModel()
    {
        var loggerMock = new Mock<Microsoft.Extensions.Logging.ILogger<OhrbobApiService>>();
        var optionsMock = Microsoft.Extensions.Options.Options.Create(new Ohrbob.Web.Configuration.OhrbobApiOptions { BaseUrl = "https://test.api/", ApiKey = "k" });
        var factoryMock = new Mock<IHttpClientFactory>();
        var accountMock = new Mock<AccountApiService>(loggerMock.Object, optionsMock, factoryMock.Object);
        var stationsMock = new Mock<StationsApiService>(loggerMock.Object, optionsMock, factoryMock.Object);
        var profilesMock = new Mock<ProfilesApiService>(loggerMock.Object, optionsMock, factoryMock.Object);

        accountMock.Setup(s => s.GetAccountAsync("a1")).ReturnsAsync(new Account { Id = "a1", Tier = SubscriptionTier.Free });
        accountMock.Setup(s => s.GetLimitsAsync("a1")).ReturnsAsync(new TierLimits { MaxStations = 2, MaxFeedsPerStation = 10, MaxProfiles = 1, StreamEnabled = false });
        stationsMock.Setup(s => s.GetStationsByAccountIdAsync("a1")).ReturnsAsync(new List<Ohrbob.Server.Shared.Requests.StationWithFeedsDto>());
        profilesMock.Setup(s => s.GetProfilesByAccountIdAsync("a1")).ReturnsAsync(new List<Profile>());

        var controller = new AccountController(accountMock.Object, stationsMock.Object, profilesMock.Object);
        var claims = new List<Claim> { new Claim(Consts.OhrbobAccountIdClaim, "a1") };
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "t")) } };

        var result = await controller.Index() as ViewResult;
        Assert.That(result, Is.Not.Null);
        var model = result.Model as Ohrbob.Web.Models.ManageAccountViewModel;
        Assert.That(model, Is.Not.Null);
        Assert.That(model.Tier, Is.EqualTo(SubscriptionTier.Free));
    }
}
