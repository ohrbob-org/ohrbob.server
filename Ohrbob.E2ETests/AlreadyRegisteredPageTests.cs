using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;
using Ohrbob.E2ETests.Infrastructure;

namespace Ohrbob.E2ETests;

[NonParallelizable]
public class AlreadyRegisteredPageTests : PageTest
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
    public async Task AlreadyRegisteredPage_ExplainsIdentityProviderConflict()
    {
        _factory.CreateClient();
        await Page.GotoAsync($"{_factory.ServerAddress}/Home/AlreadyRegistered");

        await Expect(Page.Locator("h1")).ToContainTextAsync("Already registered");
        await Expect(Page.Locator("body")).ToContainTextAsync("already registered with another identity provider");
    }
}
