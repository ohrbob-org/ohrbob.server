using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web;
using Ohrbob.Web.Configuration;
using Ohrbob.Web.Controllers;
using Ohrbob.Web.Models;
using Ohrbob.Web.Services;

namespace Ohrbob.Web.Tests;

public class ConnectControllerTests
{
    private const string AccountId = "a1";
    private const string FrontendPlayerUrl = "https://player.ohrbob.org";
    private const string ServerToken = "srv-token-123";

    private static ConnectController CreateController(
        List<Profile>? profiles,
        Action<ApiKey>? onCreate = null,
        Func<ApiKey, ApiKey?>? createResult = null)
    {
        var logger = new Mock<ILogger<OhrbobApiService>>();
        var options = Options.Create(new OhrbobApiOptions
        {
            BaseUrl = "https://test.api/",
            ApiKey = "k",
            PlayerPublicUrl = FrontendPlayerUrl
        });
        var factory = new Mock<IHttpClientFactory>();

        var profilesMock = new Mock<ProfilesApiService>(logger.Object, options, factory.Object);
        profilesMock.Setup(s => s.GetProfilesByAccountIdAsync(AccountId)).ReturnsAsync(profiles);

        // The POST action (added in a later task) creates API keys; the mock is wired here so that
        // task can observe the created key through the optional onCreate callback, and so the
        // failure branches can be exercised through the optional createResult override.
        var apiKeysMock = new Mock<ApiKeysApiService>(logger.Object, options, factory.Object);
        apiKeysMock.Setup(s => s.CreateApiKeyAsync(AccountId, It.IsAny<ApiKey>()))
            .ReturnsAsync((string _, ApiKey key) =>
            {
                onCreate?.Invoke(key);
                if (createResult != null) return createResult(key);
                key.Token = ServerToken;
                return key;
            });

        var controller = new ConnectController(profilesMock.Object, apiKeysMock.Object, options);

        var claims = new List<Claim> { new Claim(Consts.OhrbobAccountIdClaim, AccountId) };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "t")) }
        };

        var tempDataProvider = new Mock<ITempDataProvider>();
        tempDataProvider.Setup(p => p.LoadTempData(It.IsAny<HttpContext>()))
            .Returns(new Dictionary<string, object>());
        controller.TempData = new TempDataDictionary(controller.HttpContext, tempDataProvider.Object);
        return controller;
    }

    [Test]
    public async Task App_Get_WhenLoggedIn_ReturnsViewWithProfiles()
    {
        var profiles = new List<Profile> { new() { Id = "p1", AccountId = AccountId, Name = "Main" } };
        var controller = CreateController(profiles);

        var result = await controller.App();

        var view = result as ViewResult;
        Assert.That(view, Is.Not.Null);
        var model = view!.Model as ConnectAppViewModel;
        Assert.That(model, Is.Not.Null);
        Assert.That(model!.Profiles.Select(p => p.Id), Is.EqualTo(new[] { "p1" }));
    }

    [Test]
    public async Task App_Get_WhenNoAccountClaim_ReturnsForbid()
    {
        var controller = CreateController(new List<Profile>());
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        var result = await controller.App();

        Assert.That(result, Is.InstanceOf<ForbidResult>());
    }

    [Test]
    public async Task App_Post_CreatesKeyWithScopeAndReturnsDeepLink()
    {
        ApiKey? created = null;
        var profiles = new List<Profile> { new() { Id = "p1", AccountId = AccountId, Name = "Main" } };
        var controller = CreateController(profiles, onCreate: k => created = k);

        var result = await controller.App("Living room", "p1");

        Assert.That(created!.ProfileId, Is.EqualTo("p1"));
        Assert.That(created.Name, Is.EqualTo("Living room"));
        var view = result as ViewResult;
        Assert.That(view!.ViewName, Is.EqualTo("Handoff"));
        var deepLink = (string)view.Model!;
        Assert.That(deepLink, Does.StartWith("ohrbob://connect?server="));
        Assert.That(deepLink, Does.Contain("token=" + ServerToken));
        Assert.That(deepLink, Does.Contain("server=" + Uri.EscapeDataString(FrontendPlayerUrl)));
    }

    [Test]
    public async Task App_Post_AccountLevelScope_LeavesProfileIdEmpty()
    {
        ApiKey? created = null;
        var controller = CreateController(new List<Profile>(), onCreate: k => created = k);

        await controller.App("Kitchen", "");

        Assert.That(created!.ProfileId, Is.EqualTo(string.Empty));
    }

    [Test]
    public async Task App_Post_BlankName_DoesNotCreateKeyAndReturnsCard()
    {
        var createdKeys = 0;
        var controller = CreateController(new List<Profile>(), onCreate: _ => createdKeys++);

        var result = await controller.App("   ", "");

        Assert.That(createdKeys, Is.EqualTo(0));
        Assert.That(result, Is.InstanceOf<ViewResult>());
    }

    [Test]
    public async Task App_Post_ApiReturnsNull_ReturnsCardWithError()
    {
        var profiles = new List<Profile> { new() { Id = "p1", AccountId = AccountId, Name = "Main" } };
        var controller = CreateController(profiles, createResult: _ => null);

        var result = await controller.App("Living room", "p1");

        var view = result as ViewResult;
        Assert.That(view, Is.Not.Null);
        Assert.That(view!.Model, Is.InstanceOf<ConnectAppViewModel>());
        Assert.That(controller.TempData["Error"], Is.EqualTo("Failed to create API key."));
    }

    [Test]
    public async Task App_Post_ApiThrows_ReturnsCardWithError()
    {
        var profiles = new List<Profile> { new() { Id = "p1", AccountId = AccountId, Name = "Main" } };
        var controller = CreateController(profiles, createResult: _ => throw new HttpRequestException("boom"));

        var result = await controller.App("Living room", "p1");

        var view = result as ViewResult;
        Assert.That(view, Is.Not.Null);
        Assert.That(view!.Model, Is.InstanceOf<ConnectAppViewModel>());
        Assert.That(controller.TempData["Error"], Is.EqualTo("Failed to create API key."));
    }

    [Test]
    public async Task App_Get_WhenProfilesApiReturnsNull_StillRendersCard()
    {
        var controller = CreateController(null);

        var result = await controller.App();

        var view = result as ViewResult;
        Assert.That(view, Is.Not.Null);
        var model = view!.Model as ConnectAppViewModel;
        Assert.That(model, Is.Not.Null);
        Assert.That(model!.Profiles, Is.Empty);
    }

    [Test]
    public async Task App_Post_WhenNoAccountClaim_ReturnsForbid()
    {
        var createdKeys = 0;
        var controller = CreateController(new List<Profile>(), onCreate: _ => createdKeys++);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        var result = await controller.App("Living room", "");

        Assert.That(result, Is.InstanceOf<ForbidResult>());
        Assert.That(createdKeys, Is.EqualTo(0));
    }

    [Test]
    public async Task App_Post_ProfileIdNotInAccount_DoesNotCreateKeyAndReturnsCardWithError()
    {
        var createdKeys = 0;
        var profiles = new List<Profile> { new() { Id = "p1", AccountId = AccountId, Name = "Mine" } };
        var controller = CreateController(profiles, onCreate: _ => createdKeys++);

        var result = await controller.App("Living room", "p-other-account");

        Assert.That(createdKeys, Is.EqualTo(0));
        var view = result as ViewResult;
        Assert.That(view, Is.Not.Null);
        Assert.That(view!.Model, Is.InstanceOf<ConnectAppViewModel>());
        Assert.That(controller.TempData["Error"], Is.Not.Null);
    }

    [Test]
    public async Task App_Post_ErrorPath_CarriesSubmittedNameAndScopeBackToCard()
    {
        var profiles = new List<Profile> { new() { Id = "p1", AccountId = AccountId, Name = "Mine" } };
        var controller = CreateController(profiles);

        var result = await controller.App("   ", "p1");

        var view = result as ViewResult;
        Assert.That(view, Is.Not.Null);
        var model = view!.Model as ConnectAppViewModel;
        Assert.That(model, Is.Not.Null);
        Assert.That(model!.Name, Is.EqualTo("   "));
        Assert.That(model.ProfileId, Is.EqualTo("p1"));
    }
}
