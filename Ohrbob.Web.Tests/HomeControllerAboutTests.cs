using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Ohrbob.Web.Configuration;
using Ohrbob.Web.Controllers;
using Ohrbob.Web.Models;
using Ohrbob.Web.Services;

namespace Ohrbob.Web.Tests;

public class HomeControllerAboutTests
{
    private Mock<StationsApiService> _stations = null!;
    private Mock<ProfilesApiService> _profiles = null!;
    private IOptions<LegalOptions> _legal = null!;

    [SetUp]
    public void Setup()
    {
        var logger = new Mock<ILogger<OhrbobApiService>>();
        var opts = Options.Create(new OhrbobApiOptions { BaseUrl = "https://test.api/", ApiKey = "k" });
        var factory = new Mock<IHttpClientFactory>();
        _stations = new Mock<StationsApiService>(logger.Object, opts, factory.Object);
        _profiles = new Mock<ProfilesApiService>(logger.Object, opts, factory.Object);
        _legal = Options.Create(new LegalOptions());
    }

    [Test]
    public void Status_ReturnsView_WithWebApiAndPlayerVersions()
    {
        var cache = new BackendVersionCache();
        cache.Update("api-sha", "beta", true, "player-sha", "beta", false, DateTime.UtcNow);
        var controller = new HomeController(_stations.Object, _profiles.Object, _legal, cache, Options.Create(new AppVersionOptions { Version = "web-sha", Tag = "beta" }));
        var result = (ViewResult)controller.Status();
        var model = (AboutViewModel)result.Model!;
        Assert.That(model.WebVersion, Is.EqualTo("web-sha"));
        Assert.That(model.ApiVersion, Is.EqualTo("api-sha"));
        Assert.That(model.PlayerReachable, Is.False);
    }
}
