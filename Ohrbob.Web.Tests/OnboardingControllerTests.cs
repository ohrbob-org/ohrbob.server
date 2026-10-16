using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web;
using Ohrbob.Web.Configuration;
using Ohrbob.Web.Controllers;
using Ohrbob.Web.Services;

namespace Ohrbob.Web.Tests;

public class OnboardingControllerTests
{
    private const string AccountId = "a1";

    private static OnboardingController CreateController(
        Mock<ProfilesApiService> profilesMock,
        Mock<StationsApiService> stationsMock,
        Mock<AccountApiService> accountMock)
    {
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
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "t")) }
        };
        return controller;
    }

    [Test]
    public async Task CreateProfile_WhenAccountAlreadyHasProfile_ReusesExistingProfileInsteadOfCreatingDuplicate()
    {
        var logger = new Mock<ILogger<OhrbobApiService>>();
        var options = Options.Create(new OhrbobApiOptions { BaseUrl = "https://test.api/", ApiKey = "k" });
        var factory = new Mock<IHttpClientFactory>();
        var profilesMock = new Mock<ProfilesApiService>(logger.Object, options, factory.Object);
        var stationsMock = new Mock<StationsApiService>(logger.Object, options, factory.Object);
        var accountMock = new Mock<AccountApiService>(logger.Object, options, factory.Object);

        // The account already has the profile created during the first (aborted) onboarding attempt.
        var existing = new Profile { Id = "p1", AccountId = AccountId, Name = "First", StreamToken = "t1" };
        profilesMock.Setup(s => s.GetProfilesByAccountIdAsync(AccountId)).ReturnsAsync(new List<Profile> { existing });
        profilesMock.Setup(s => s.CreateProfileAsync(AccountId, It.IsAny<Profile>()))
            .ReturnsAsync(new Profile { Id = "p2", AccountId = AccountId, Name = "Retry", StreamToken = "t2" });

        var controller = CreateController(profilesMock, stationsMock, accountMock);

        var result = await controller.CreateProfile("Retry", true);

        profilesMock.Verify(s => s.CreateProfileAsync(It.IsAny<string>(), It.IsAny<Profile>()), Times.Never);
        var redirect = result as RedirectToActionResult;
        Assert.That(redirect, Is.Not.Null);
        Assert.That(redirect!.ActionName, Is.EqualTo("Station"));
        Assert.That(redirect.RouteValues!["profileId"], Is.EqualTo("p1"));
    }

    [Test]
    public async Task CreateProfile_WhenAccountHasNoProfile_CreatesTheFirstProfile()
    {
        var logger = new Mock<ILogger<OhrbobApiService>>();
        var options = Options.Create(new OhrbobApiOptions { BaseUrl = "https://test.api/", ApiKey = "k" });
        var factory = new Mock<IHttpClientFactory>();
        var profilesMock = new Mock<ProfilesApiService>(logger.Object, options, factory.Object);
        var stationsMock = new Mock<StationsApiService>(logger.Object, options, factory.Object);
        var accountMock = new Mock<AccountApiService>(logger.Object, options, factory.Object);

        profilesMock.Setup(s => s.GetProfilesByAccountIdAsync(AccountId)).ReturnsAsync(new List<Profile>());
        profilesMock.Setup(s => s.CreateProfileAsync(AccountId, It.IsAny<Profile>()))
            .ReturnsAsync(new Profile { Id = "p2", AccountId = AccountId, Name = "First", StreamToken = "t2" });

        var controller = CreateController(profilesMock, stationsMock, accountMock);

        var result = await controller.CreateProfile("First", true);

        profilesMock.Verify(s => s.CreateProfileAsync(AccountId, It.IsAny<Profile>()), Times.Once);
        var redirect = result as RedirectToActionResult;
        Assert.That(redirect, Is.Not.Null);
        Assert.That(redirect!.ActionName, Is.EqualTo("Station"));
        Assert.That(redirect.RouteValues!["profileId"], Is.EqualTo("p2"));
    }
}
