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
using Ohrbob.Web.Services;

namespace Ohrbob.Web.Tests;

public class ProfilesControllerTests
{
    private const string TestAccountId = "test-account-id";
    private Mock<ProfilesApiService> _profiles = null!;
    private Mock<StationsApiService> _stations = null!;
    private Mock<AccountApiService> _accounts = null!;

    [SetUp]
    public void Setup()
    {
        var logger = new Mock<ILogger<OhrbobApiService>>();
        var opts = Options.Create(new OhrbobApiOptions { BaseUrl = "https://test.api/", ApiKey = "k" });
        var factory = new Mock<IHttpClientFactory>();
        _profiles = new Mock<ProfilesApiService>(logger.Object, opts, factory.Object);
        _stations = new Mock<StationsApiService>(logger.Object, opts, factory.Object);
        _accounts = new Mock<AccountApiService>(logger.Object, opts, factory.Object);
    }

    private ProfilesController CreateController()
    {
        var controller = new ProfilesController(_profiles.Object, _stations.Object, _accounts.Object);
        var claims = new List<Claim> { new(Consts.OhrbobAccountIdClaim, TestAccountId) };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
            }
        };
        return controller;
    }

    [Test]
    public async Task EditProfile_Post_PreservesSubscribedStationIds()
    {
        var existing = new Profile
        {
            Id = "p1",
            AccountId = TestAccountId,
            Name = "Old",
            StreamToken = "token",
            SubscribedStationIds = new[] { "st-1", "st-2" }
        };
        _profiles.Setup(p => p.GetProfileByIdAsync("p1")).ReturnsAsync(existing);

        var posted = new Profile
        {
            Id = "p1",
            AccountId = TestAccountId,
            Name = "Renamed",
            SubscribedStationIds = Array.Empty<string>()
        };

        var result = await CreateController().Edit("p1", posted, new List<string>());

        Assert.That(result, Is.InstanceOf<RedirectToActionResult>());
        _profiles.Verify(p => p.UpdateProfileAsync(It.Is<Profile>(profile =>
            profile.Name == "Renamed" &&
            profile.StreamToken == "token" &&
            profile.SubscribedStationIds.SequenceEqual(new[] { "st-1", "st-2" }))), Times.Once);
    }
}
