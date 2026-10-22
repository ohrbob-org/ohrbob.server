using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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

    private static ConnectController CreateController(List<Profile> profiles, Action<ApiKey>? onCreate = null)
    {
        var logger = new Mock<ILogger<OhrbobApiService>>();
        var options = Options.Create(new OhrbobApiOptions { BaseUrl = "https://test.api/", ApiKey = "k" });
        var factory = new Mock<IHttpClientFactory>();

        var profilesMock = new Mock<ProfilesApiService>(logger.Object, options, factory.Object);
        profilesMock.Setup(s => s.GetProfilesByAccountIdAsync(AccountId)).ReturnsAsync(profiles);

        // The POST action (added in a later task) creates API keys; the mock is wired here so that
        // task can observe the created key through the optional onCreate callback.
        var apiKeysMock = new Mock<ApiKeysApiService>(logger.Object, options, factory.Object);
        apiKeysMock.Setup(s => s.CreateApiKeyAsync(AccountId, It.IsAny<ApiKey>()))
            .ReturnsAsync((string _, ApiKey key) =>
            {
                onCreate?.Invoke(key);
                return key;
            });

        var controller = new ConnectController(profilesMock.Object, options);

        var claims = new List<Claim> { new Claim(Consts.OhrbobAccountIdClaim, AccountId) };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "t")) }
        };
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
}
