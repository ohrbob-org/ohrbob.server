using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web;
using Ohrbob.Web.Configuration;
using Ohrbob.Web.Controllers;
using Ohrbob.Web.Filters;
using Ohrbob.Web.Services;

namespace Ohrbob.Web.Tests;

/// <summary>
/// Covers carrying a local return URL (e.g. /connect/app) through the onboarding flow.
/// </summary>
public class OnboardingReturnUrlTests
{
    private const string AccountId = "a1";
    private const string ReturnUrl = "/connect/app";

    private static Mock<AccountApiService> CreateAccountMock(Account? account = null)
    {
        var logger = new Mock<ILogger<OhrbobApiService>>();
        var options = Options.Create(new OhrbobApiOptions { BaseUrl = "https://test.api/", ApiKey = "k" });
        var mock = new Mock<AccountApiService>(logger.Object, options, CreateFactory().Object);
        if (account != null)
        {
            mock.Setup(s => s.GetAccountAsync(AccountId)).ReturnsAsync(account);
        }
        return mock;
    }

    /// <summary>A factory whose clients succeed, so non-virtual service calls don't hit the network.</summary>
    private static Mock<IHttpClientFactory> CreateFactory()
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(new OkHandler()));
        return factory;
    }

    private static OnboardingController CreateController(
        Mock<AccountApiService> accountMock,
        Mock<ProfilesApiService>? profilesMock = null,
        Mock<StationsApiService>? stationsMock = null)
    {
        var logger = new Mock<ILogger<OhrbobApiService>>();
        var options = Options.Create(new OhrbobApiOptions { BaseUrl = "https://test.api/", ApiKey = "k" });
        var factory = CreateFactory();
        profilesMock ??= new Mock<ProfilesApiService>(logger.Object, options, factory.Object);
        stationsMock ??= new Mock<StationsApiService>(logger.Object, options, factory.Object);

        var podcast = new PodcastIndexApiService(
            new HttpClient(),
            new ConfigurationBuilder().Build(),
            new Mock<ILogger<PodcastIndexApiService>>().Object);

        var controller = new OnboardingController(
            profilesMock.Object,
            stationsMock.Object,
            accountMock.Object,
            podcast,
            Options.Create(new LegalOptions { Show = false }));

        var claims = new List<Claim> { new Claim(Consts.OhrbobAccountIdClaim, AccountId) };
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "t")) };
        // Url.IsLocalUrl needs IUrlHelperFactory, normally provided by the framework's DI container.
        httpContext.RequestServices = new ServiceCollection()
            .AddSingleton<IUrlHelperFactory, UrlHelperFactory>()
            .BuildServiceProvider();

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext, RouteData = new RouteData() };
        // Controller.View() also snapshots TempData; supply one so ViewResult creation works.
        controller.TempData = new TempDataDictionary(httpContext, new Mock<ITempDataProvider>().Object);
        return controller;
    }

    private static ActionExecutingContext CreateFilterContext(string path, bool htmx = false)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = path;
        if (htmx) httpContext.Request.Headers["HX-Request"] = "true";

        var claims = new List<Claim> { new Claim(Consts.OhrbobAccountIdClaim, AccountId) };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "t"));

        var routeData = new RouteData();
        routeData.Values["controller"] = "Connect";
        routeData.Values["action"] = "App";

        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());
        return new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            controller: null!);
    }

    /// <summary>A delegate that fails the test if the filter lets the request through.</summary>
    private static ActionExecutionDelegate NextMustNotRun() =>
        () => throw new InvalidOperationException("the un-onboarded request must not reach the action");

    [Test]
    public async Task Filter_WhenAccountNeedsOnboarding_RedirectsToOnboardingWithReturnUrl()
    {
        var accountMock = CreateAccountMock(new Account { Id = AccountId, NeedsOnboarding = true });
        var filter = new OnboardingFilter(accountMock.Object);
        var context = CreateFilterContext(ReturnUrl);

        await filter.OnActionExecutionAsync(context, NextMustNotRun());

        var redirect = context.Result as RedirectToActionResult;
        Assert.That(redirect, Is.Not.Null);
        Assert.That(redirect!.ActionName, Is.EqualTo("Index"));
        Assert.That(redirect.ControllerName, Is.EqualTo("Onboarding"));
        Assert.That(redirect.RouteValues!["returnUrl"], Is.EqualTo(ReturnUrl));
    }

    [Test]
    public async Task Filter_WhenHtmxRequest_RedirectHeaderCarriesReturnUrl()
    {
        var accountMock = CreateAccountMock(new Account { Id = AccountId, NeedsOnboarding = true });
        var filter = new OnboardingFilter(accountMock.Object);
        var context = CreateFilterContext(ReturnUrl, htmx: true);

        await filter.OnActionExecutionAsync(context, NextMustNotRun());

        Assert.That(context.Result, Is.InstanceOf<OkResult>());
        Assert.That(
            context.HttpContext.Response.Headers["HX-Redirect"].ToString(),
            Is.EqualTo("/Onboarding?returnUrl=%2Fconnect%2Fapp"));
    }

    [Test]
    public async Task Index_WhenReturnUrlGiven_CarriesItForward()
    {
        var accountMock = CreateAccountMock(new Account { Id = AccountId, NeedsOnboarding = true });
        var controller = CreateController(accountMock);

        var result = await controller.Index(returnUrl: ReturnUrl) as ViewResult;

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ViewData["ReturnUrl"], Is.EqualTo(ReturnUrl));
    }

    [Test]
    public async Task Finish_WhenReturnUrlIsLocal_RedirectsToIt()
    {
        var accountMock = CreateAccountMock(new Account { Id = AccountId, NeedsOnboarding = true });
        var logger = new Mock<ILogger<OhrbobApiService>>();
        var options = Options.Create(new OhrbobApiOptions { BaseUrl = "https://test.api/", ApiKey = "k" });
        var factory = CreateFactory();
        var profilesMock = new Mock<ProfilesApiService>(logger.Object, options, factory.Object);
        var stationsMock = new Mock<StationsApiService>(logger.Object, options, factory.Object);
        stationsMock.Setup(s => s.CreateStationAsync(AccountId, It.IsAny<Station>()))
            .ReturnsAsync(new Station { Id = "s1", AccountId = AccountId, Name = "My Station" });
        profilesMock.Setup(s => s.AddStationToProfileAsync("p1", "s1")).Returns(Task.CompletedTask);

        var controller = CreateController(accountMock, profilesMock, stationsMock);

        var result = await controller.Finish(new List<string>(), "p1", "My Station", returnUrl: ReturnUrl);

        var redirect = result as LocalRedirectResult;
        Assert.That(redirect, Is.Not.Null, "a local returnUrl should redirect straight to it");
        Assert.That(redirect!.Url, Is.EqualTo(ReturnUrl));
    }

    [Test]
    public async Task Finish_WhenReturnUrlIsExternal_IsIgnored()
    {
        var accountMock = CreateAccountMock(new Account { Id = AccountId, NeedsOnboarding = true });
        var logger = new Mock<ILogger<OhrbobApiService>>();
        var options = Options.Create(new OhrbobApiOptions { BaseUrl = "https://test.api/", ApiKey = "k" });
        var factory = CreateFactory();
        var profilesMock = new Mock<ProfilesApiService>(logger.Object, options, factory.Object);
        var stationsMock = new Mock<StationsApiService>(logger.Object, options, factory.Object);
        stationsMock.Setup(s => s.CreateStationAsync(AccountId, It.IsAny<Station>()))
            .ReturnsAsync(new Station { Id = "s1", AccountId = AccountId, Name = "My Station" });
        profilesMock.Setup(s => s.AddStationToProfileAsync("p1", "s1")).Returns(Task.CompletedTask);

        var controller = CreateController(accountMock, profilesMock, stationsMock);

        var result = await controller.Finish(new List<string>(), "p1", "My Station", returnUrl: "https://evil.example");

        var redirect = result as RedirectToActionResult;
        Assert.That(redirect, Is.Not.Null, "an external returnUrl must fall back to the default destination");
        Assert.That(redirect!.ActionName, Is.EqualTo("Index"));
        Assert.That(redirect.ControllerName, Is.EqualTo("Home"));
    }

    [Test]
    public async Task Finish_WhenReturnUrlIsProtocolRelativeExternal_IsIgnored()
    {
        var accountMock = CreateAccountMock(new Account { Id = AccountId, NeedsOnboarding = true });
        var logger = new Mock<ILogger<OhrbobApiService>>();
        var options = Options.Create(new OhrbobApiOptions { BaseUrl = "https://test.api/", ApiKey = "k" });
        var factory = CreateFactory();
        var profilesMock = new Mock<ProfilesApiService>(logger.Object, options, factory.Object);
        var stationsMock = new Mock<StationsApiService>(logger.Object, options, factory.Object);
        stationsMock.Setup(s => s.CreateStationAsync(AccountId, It.IsAny<Station>()))
            .ReturnsAsync(new Station { Id = "s1", AccountId = AccountId, Name = "My Station" });
        profilesMock.Setup(s => s.AddStationToProfileAsync("p1", "s1")).Returns(Task.CompletedTask);

        var controller = CreateController(accountMock, profilesMock, stationsMock);

        // A protocol-relative URL is treated as external by Url.IsLocalUrl and must not be followed.
        var result = await controller.Finish(new List<string>(), "p1", "My Station", returnUrl: "//evil.example");

        Assert.That(result, Is.Not.InstanceOf<LocalRedirectResult>());
        var redirect = result as RedirectToActionResult;
        Assert.That(redirect, Is.Not.Null, "a protocol-relative returnUrl must fall back to the default destination");
        Assert.That(redirect!.ActionName, Is.EqualTo("Index"));
        Assert.That(redirect.ControllerName, Is.EqualTo("Home"));
    }

    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
    }
}
