using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Moq;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web;
using Ohrbob.Web.Services;
using Ohrbob.Web.ViewComponents;

namespace Ohrbob.Web.Tests;

public class ProfileSelectorTierTests
{
    private Mock<ProfilesApiService> _profilesMock = null!;
    private Mock<AccountApiService> _accountMock = null!;
    private ProfileSelectorViewComponent _component = null!;
    private const string TestAccountId = "test-account-id";

    [SetUp]
    public void Setup()
    {
        var loggerMock = new Mock<Microsoft.Extensions.Logging.ILogger<OhrbobApiService>>();
        var optionsMock = Microsoft.Extensions.Options.Options.Create(new Ohrbob.Web.Configuration.OhrbobApiOptions
        {
            BaseUrl = "https://test.api/",
            ApiKey = "test-api-key"
        });
        var clientFactoryMock = new Mock<IHttpClientFactory>();

        _profilesMock = new Mock<ProfilesApiService>(loggerMock.Object, optionsMock, clientFactoryMock.Object);
        _accountMock = new Mock<AccountApiService>(loggerMock.Object, optionsMock, clientFactoryMock.Object);
        _component = new ProfileSelectorViewComponent(_profilesMock.Object, _accountMock.Object);
    }

    private void SetupHttpContext(string? activeProfileCookie)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(Consts.OhrbobAccountIdClaim, TestAccountId) }, "TestAuth"));
        if (activeProfileCookie != null)
            httpContext.Request.Headers.Cookie = $"Ohrbob_ActiveProfileId={activeProfileCookie}";

        var viewContext = new ViewContext
        {
            HttpContext = httpContext,
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        };
        _component.ViewComponentContext = new ViewComponentContext
        {
            ViewContext = viewContext
        };
    }

    [Test]
    public async Task InvokeAsync_ExposesAccountTierInModel()
    {
        var profile = new Profile { Id = "p1", AccountId = TestAccountId, Name = "Main" };
        _profilesMock.Setup(s => s.GetProfilesByAccountIdAsync(TestAccountId))
            .ReturnsAsync(new List<Profile> { profile });
        _accountMock.Setup(s => s.GetAccountAsync(TestAccountId))
            .ReturnsAsync(new Account { Id = TestAccountId, Tier = SubscriptionTier.Morebob });
        SetupHttpContext("p1");

        var result = await _component.InvokeAsync() as ViewViewComponentResult;

        Assert.That(result, Is.Not.Null);
        var model = result.ViewData?.Model as ProfileSelectorViewModel;
        Assert.That(model, Is.Not.Null);
        Assert.That(model.Tier, Is.EqualTo(SubscriptionTier.Morebob));
    }

    [Test]
    public async Task InvokeAsync_NoProfiles_StillExposesTier()
    {
        _profilesMock.Setup(s => s.GetProfilesByAccountIdAsync(TestAccountId))
            .ReturnsAsync(new List<Profile>());
        _accountMock.Setup(s => s.GetAccountAsync(TestAccountId))
            .ReturnsAsync(new Account { Id = TestAccountId, Tier = SubscriptionTier.Free });
        SetupHttpContext(null);

        var result = await _component.InvokeAsync() as ViewViewComponentResult;

        Assert.That(result, Is.Not.Null);
        var model = result.ViewData?.Model as ProfileSelectorViewModel;
        Assert.That(model, Is.Not.Null);
        Assert.That(model.Tier, Is.EqualTo(SubscriptionTier.Free));
    }
}
