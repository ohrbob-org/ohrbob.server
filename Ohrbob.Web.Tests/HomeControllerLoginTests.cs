using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Ohrbob.Web.Configuration;
using Ohrbob.Web.Controllers;
using Ohrbob.Web.Services;

namespace Ohrbob.Web.Tests;

public class HomeControllerLoginTests
{
    private HomeController BuildController(Dictionary<string, string?> configValues)
    {
        var logger = new Mock<ILogger<OhrbobApiService>>();
        var opts = Options.Create(new OhrbobApiOptions { BaseUrl = "https://test.api/", ApiKey = "k" });
        var factory = new Mock<IHttpClientFactory>();
        var stations = new Mock<StationsApiService>(logger.Object, opts, factory.Object);
        var profiles = new Mock<ProfilesApiService>(logger.Object, opts, factory.Object);
        var legal = Options.Create(new LegalOptions());
        var cache = new BackendVersionCache();
        var appVersion = Options.Create(new AppVersionOptions { Version = "v", Tag = "t" });
        var controller = new HomeController(stations.Object, profiles.Object, legal, cache, appVersion);
        var config = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();
        var http = new DefaultHttpContext();
        http.RequestServices = new ServiceCollection()
            .AddSingleton<IConfiguration>(config)
            .BuildServiceProvider();
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        controller.Url = Mock.Of<IUrlHelper>(u => u.IsLocalUrl(It.IsAny<string>()) == true);
        controller.TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>());
        return controller;
    }

    [Test]
    public void Login_NoProvider_ReturnsView_AndGitHubDisabled_WhenUnconfigured()
    {
        var controller = BuildController(new Dictionary<string, string?>());
        var result = controller.Login(null!) as ViewResult;
        Assert.That(result, Is.Not.Null);
        Assert.That(controller.ViewBag.GitHubEnabled, Is.EqualTo(false));
        Assert.That(controller.ViewBag.NextcloudEnabled, Is.EqualTo(false));
        Assert.That(controller.ViewBag.GoogleEnabled, Is.EqualTo(false));
        Assert.That(controller.ViewBag.AmazonEnabled, Is.EqualTo(false));
    }

    [Test]
    public void Login_NoProvider_ReturnsView_AndAmazonEnabled_WhenConfigured()
    {
        var controller = BuildController(new Dictionary<string, string?>
        {
            ["Oidc:Amazon:ClientId"] = "id",
            ["Oidc:Amazon:ClientSecret"] = "secret"
        });
        var result = controller.Login(null!) as ViewResult;
        Assert.That(result, Is.Not.Null);
        Assert.That(controller.ViewBag.AmazonEnabled, Is.EqualTo(true));
    }

    [Test]
    public void Login_AmazonProvider_WithRegion_ChallengesWithRegionDomain()
    {
        var controller = BuildController(new Dictionary<string, string?>
        {
            ["Oidc:Amazon:ClientId"] = "id",
            ["Oidc:Amazon:ClientSecret"] = "secret"
        });
        var result = controller.Login("Amazon", "/", "de") as ChallengeResult;
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Properties!.Items["amazon_domain"], Is.EqualTo("amazon.de"));
    }

    [Test]
    public void Login_AmazonProvider_WithUnknownRegion_ReturnsView_NotChallenge()
    {
        var controller = BuildController(new Dictionary<string, string?>
        {
            ["Oidc:Amazon:ClientId"] = "id",
            ["Oidc:Amazon:ClientSecret"] = "secret"
        });
        var result = controller.Login("Amazon", "/", "not-a-marketplace");
        Assert.That(result, Is.InstanceOf<ViewResult>());
    }

    [Test]
    public void AmazonRegions_Contains_TenDistinctMarkets()
    {
        Assert.That(OidcProviderConfig.AmazonRegions, Has.Length.EqualTo(10));
        Assert.That(OidcProviderConfig.AmazonRegions.Select(r => r.Domain), Is.Unique);
    }

    [Test]
    public void Login_AmazonProvider_WhenUnconfigured_ReturnsView_NotChallenge()
    {
        var controller = BuildController(new Dictionary<string, string?>());
        var result = controller.Login("Amazon");
        Assert.That(result, Is.InstanceOf<ViewResult>());
    }

    [Test]
    public void Login_NoProvider_ReturnsView_AndOnlyNextcloudEnabled_WhenConfigured()
    {
        var controller = BuildController(new Dictionary<string, string?>
        {
            ["Oidc:Nextcloud:Authority"] = "https://nextcloud.test",
            ["Oidc:Nextcloud:ClientId"] = "id",
            ["Oidc:Nextcloud:ClientSecret"] = "secret"
        });
        var result = controller.Login(null!) as ViewResult;
        Assert.That(result, Is.Not.Null);
        Assert.That(controller.ViewBag.NextcloudEnabled, Is.EqualTo(true));
        Assert.That(controller.ViewBag.GoogleEnabled, Is.EqualTo(false));
        Assert.That(controller.ViewBag.GitHubEnabled, Is.EqualTo(false));
        Assert.That(controller.ViewBag.AmazonEnabled, Is.EqualTo(false));
    }

    [Test]
    public void Login_NextcloudProvider_WhenUnconfigured_ReturnsView_NotChallenge()
    {
        var controller = BuildController(new Dictionary<string, string?>());
        var result = controller.Login("Nextcloud");
        Assert.That(result, Is.InstanceOf<ViewResult>());
    }

    [Test]
    public void Login_NoProvider_ReturnsView_AndGitHubEnabled_WhenConfigured()
    {
        var controller = BuildController(new Dictionary<string, string?>
        {
            ["Oidc:GitHub:ClientId"] = "id",
            ["Oidc:GitHub:ClientSecret"] = "secret"
        });
        var result = controller.Login(null!) as ViewResult;
        Assert.That(result, Is.Not.Null);
        Assert.That(controller.ViewBag.GitHubEnabled, Is.EqualTo(true));
    }

    [Test]
    public void Login_GitHubProvider_WhenUnconfigured_ReturnsView_NotChallenge()
    {
        var controller = BuildController(new Dictionary<string, string?>());
        var result = controller.Login("GitHub");
        Assert.That(result, Is.InstanceOf<ViewResult>());
    }
}
