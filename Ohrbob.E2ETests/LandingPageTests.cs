using Microsoft.Playwright;
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

        // Check that unauthenticated user sees the footer and its links
        await Expect(Page.Locator("footer")).ToBeVisibleAsync();
        await Expect(Page.Locator("footer >> text=About")).ToBeVisibleAsync();
        await Expect(Page.Locator("footer >> a[href*='github.com']")).ToBeVisibleAsync();
    }

    [Test]
    public async Task LandingPage_ContentIsVisibleWithoutScrollReveal()
    {
        _factory.CreateClient(); // Start the server
        await Page.GotoAsync(_factory.ServerAddress);

        await Expect(Page.Locator("h1#landing-title")).ToBeVisibleAsync();

        // The feature copy must be readable right away, not faded in on scroll.
        var textBox = Page.Locator(".step-text-box").First;
        await Expect(textBox).ToBeVisibleAsync();
        var opacity = await textBox.EvaluateAsync<string>("el => getComputedStyle(el).opacity");
        Assert.That(opacity, Is.EqualTo("1"));

        await Expect(Page.Locator("h2", new PageLocatorOptions { HasTextString = "Tune the rotation" })).ToBeVisibleAsync();
    }

    [Test]
    public async Task LandingPage_ComparesFreeAndMorebobTiers()
    {
        _factory.CreateClient(); // Start the server
        await Page.GotoAsync(_factory.ServerAddress);

        var table = Page.Locator(".landing-tier-table");
        await Expect(table).ToBeVisibleAsync();

        var header = table.Locator("thead");
        await Expect(header).ToContainTextAsync("Free");
        await Expect(header).ToContainTextAsync("morebob");

        await Expect(table.Locator("tbody tr", new LocatorLocatorOptions { HasTextString = "Radio stations" }))
            .ToContainTextAsync("Unlimited");
        await Expect(table.Locator("tbody tr", new LocatorLocatorOptions { HasTextString = "Podcasts" }))
            .ToContainTextAsync("10");
        await Expect(table.Locator("tbody tr", new LocatorLocatorOptions { HasTextString = "Listening profiles" }))
            .ToContainTextAsync("Up to 5");
        await Expect(table.Locator("tbody tr", new LocatorLocatorOptions { HasTextString = "App support" }))
            .ToContainTextAsync("Android Auto, Linux, Windows, Mac");
    }
}
