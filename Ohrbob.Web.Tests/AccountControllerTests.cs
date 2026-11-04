using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
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
        var apiKeysMock = new Mock<ApiKeysApiService>(loggerMock.Object, optionsMock, factoryMock.Object);

        accountMock.Setup(s => s.GetAccountAsync("a1")).ReturnsAsync(new Account { Id = "a1", Tier = SubscriptionTier.Free });
        accountMock.Setup(s => s.GetLimitsAsync("a1")).ReturnsAsync(new TierLimits { MaxStations = 2, MaxFeedsPerStation = 10, MaxProfiles = 1, StreamEnabled = false });
        stationsMock.Setup(s => s.GetStationsByAccountIdAsync("a1")).ReturnsAsync(new List<Ohrbob.Server.Shared.Requests.StationWithFeedsDto>());
        profilesMock.Setup(s => s.GetProfilesByAccountIdAsync("a1")).ReturnsAsync(new List<Profile>());
        apiKeysMock.Setup(s => s.GetApiKeysByAccountIdAsync("a1")).ReturnsAsync(new List<ApiKey>());

        var controller = new AccountController(accountMock.Object, stationsMock.Object, profilesMock.Object, apiKeysMock.Object);
        var claims = new List<Claim> { new Claim(Consts.OhrbobAccountIdClaim, "a1") };
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "t")) } };

        var result = await controller.Index() as ViewResult;
        Assert.That(result, Is.Not.Null);
        var model = result.Model as Ohrbob.Web.Models.ManageAccountViewModel;
        Assert.That(model, Is.Not.Null);
        Assert.That(model.Tier, Is.EqualTo(SubscriptionTier.Free));
    }

    [Test]
    public async Task Delete_ReturnsConfirmationView()
    {
        var accountMock = CreateAccountMock();

        accountMock.Setup(s => s.GetAccountAsync("a1"))
            .ReturnsAsync(new Account { Id = "a1", Email = "user@example.com" });

        var controller = CreateController(accountMock.Object);

        var result = await controller.Delete() as ViewResult;
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Model, Is.InstanceOf<Account>());
    }

    [Test]
    public async Task Delete_Post_DeletesAccountAndSignsOut()
    {
        var accountMock = CreateAccountMock();

        var authMock = new Mock<IAuthenticationService>();
        authMock.Setup(a => a.SignOutAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<AuthenticationProperties?>()))
            .Returns(Task.CompletedTask);
        var urlHelperFactory = new Mock<IUrlHelperFactory>();
        urlHelperFactory.Setup(f => f.GetUrlHelper(It.IsAny<ActionContext>()))
            .Returns(new Mock<IUrlHelper>().Object);
        var services = new ServiceCollection()
            .AddSingleton(authMock.Object)
            .AddSingleton(urlHelperFactory.Object)
            .BuildServiceProvider();

        var controller = CreateController(accountMock.Object, services);

        var result = await controller.DeleteConfirmed() as RedirectToActionResult;

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ActionName, Is.EqualTo("Index"));
        Assert.That(result.ControllerName, Is.EqualTo("Home"));
        accountMock.Verify(s => s.DeleteAccountAsync("a1"), Times.Once);
        authMock.Verify(a => a.SignOutAsync(
            It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<AuthenticationProperties?>()), Times.Once);
    }

    private static Mock<AccountApiService> CreateAccountMock()
    {
        var loggerMock = new Mock<Microsoft.Extensions.Logging.ILogger<OhrbobApiService>>();
        var optionsMock = Microsoft.Extensions.Options.Options.Create(new Ohrbob.Web.Configuration.OhrbobApiOptions { BaseUrl = "https://test.api/", ApiKey = "k" });
        var factoryMock = new Mock<IHttpClientFactory>();
        return new Mock<AccountApiService>(loggerMock.Object, optionsMock, factoryMock.Object);
    }

    private static AccountController CreateController(AccountApiService accountService, IServiceProvider? services = null)
    {
        var loggerMock = new Mock<Microsoft.Extensions.Logging.ILogger<OhrbobApiService>>();
        var optionsMock = Microsoft.Extensions.Options.Options.Create(new Ohrbob.Web.Configuration.OhrbobApiOptions { BaseUrl = "https://test.api/", ApiKey = "k" });
        var factoryMock = new Mock<IHttpClientFactory>();
        var stationsMock = new Mock<StationsApiService>(loggerMock.Object, optionsMock, factoryMock.Object);
        var profilesMock = new Mock<ProfilesApiService>(loggerMock.Object, optionsMock, factoryMock.Object);
        var apiKeysMock = new Mock<ApiKeysApiService>(loggerMock.Object, optionsMock, factoryMock.Object);

        var controller = new AccountController(accountService, stationsMock.Object, profilesMock.Object, apiKeysMock.Object);
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(Consts.OhrbobAccountIdClaim, "a1") }, "t"))
        };
        if (services != null) httpContext.RequestServices = services;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }
}
