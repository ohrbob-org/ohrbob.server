using Microsoft.Playwright.NUnit;
using NUnit.Framework;
using Ohrbob.E2ETests.Infrastructure;

namespace Ohrbob.E2ETests;

[NonParallelizable]
public class LandingPageTests : PageTest
{
    private PlaywrightWebApplicationFactory _factory = null!;

    [SetUp]
    public void Setup()
    {
        TestAuthHandler.IsAuthenticated = false;
        _factory = new PlaywrightWebApplicationFactory();
    }

    [TearDown]
    public void TearDown()
    {
        _factory.Dispose();
        TestAuthHandler.IsAuthenticated = true;
    }

    [Test]
    public async Task LandingPage_HasFloatingNav_And_SplitLayout()
    {
        _factory.CreateClient(); // Start the server
        var url = _factory.ServerAddress;
        await Page.GotoAsync(url);

        // Check for new floating nav
        await Expect(Page.Locator(".floating-nav-pill")).ToBeVisibleAsync();
        
        // Check for landing layout
        await Expect(Page.Locator(".radio-landing-container")).ToBeVisibleAsync();
        await Expect(Page.Locator(".step-text-box").First).ToBeVisibleAsync();
    }
}
