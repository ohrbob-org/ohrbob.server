using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;
using Ohrbob.E2ETests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System.IO;

namespace Ohrbob.E2ETests;

[NonParallelizable]
public class NavigationTests : PageTest
{
    private PlaywrightWebApplicationFactory _factory = null!;
    private string _testAccountId = string.Empty;

    [SetUp]
    public async Task Setup()
    {
        await Context.ClearCookiesAsync();
        _factory = new PlaywrightWebApplicationFactory();
        
        var accountService = _factory.Services.GetRequiredService<Ohrbob.Web.Services.AccountApiService>();
        var uniqueEmail = $"nav-{Guid.NewGuid()}@test.com";
        var account = await accountService.CreateOrUpdateAccountAsync("E2E Nav Test", uniqueEmail, "", "Test", Guid.NewGuid().ToString());
        
        if (account != null) {
            await accountService.CompleteOnboardingAsync(account.Id);
            var profileService = _factory.Services.GetRequiredService<Ohrbob.Web.Services.ProfilesApiService>();
            var profiles = await profileService.GetProfilesByAccountIdAsync(account.Id);
            if (profiles == null || !profiles.Any())
            {
                await profileService.CreateProfileAsync(account.Id, new Ohrbob.Server.Shared.Data.Profile { Name = "Nav Profile" });
            }
        }
        
        _testAccountId = account!.Id;
        TestAuthHandler.TestAccountId = _testAccountId;
    }

    [TearDown]
    public async Task TearDown()
    {
        await Context.CloseAsync();
        _factory.Dispose();
        TestAuthHandler.TestAccountId = string.Empty;
    }

    [TestCase(320)]
    [TestCase(360)]
    [TestCase(390)]
    [TestCase(480)]
    [TestCase(575)]
    public async Task Navbar_FitsOnNarrowSmartphone(int width)
    {
        await Page.SetViewportSizeAsync(width, 800);
        await Page.GotoAsync(_factory.ServerAddress);

        var pill = Page.Locator(".floating-nav-pill");
        await Expect(pill).ToBeVisibleAsync();

        var overflow = await Page.EvaluateAsync<bool>(@"() => {
            const pill = document.querySelector('.floating-nav-pill');
            const bounds = pill.getBoundingClientRect();
            if (pill.scrollWidth > pill.clientWidth + 1) return true;
            return Array.from(pill.querySelectorAll('a, button')).some(el => {
                const r = el.getBoundingClientRect();
                return r.left < bounds.left - 1 || r.right > bounds.right + 1;
            });
        }");

        Assert.That(overflow, Is.False, "The navigation bar must not overflow on narrow smartphone viewports.");
    }

    [Test]
    public async Task NavigateToEditAndBack_ProfileDropdownStillWorks()
    {
        // First, create a station so we have a card with a gear icon
        var feedPath = Path.Combine(Directory.GetCurrentDirectory(), "../../../../example_feeds.txt");
        if (!File.Exists(feedPath))
        {
            feedPath = Path.Combine(Directory.GetCurrentDirectory(), "../../../../../example_feeds.txt");
        }
        var feeds = await File.ReadAllLinesAsync(feedPath);
        var firstFeed = feeds.First(l => !string.IsNullOrWhiteSpace(l));

        await Page.GotoAsync(_factory.ServerAddress);
        Page.Console += (_, msg) => Console.WriteLine($"BROWSER CONSOLE: {msg.Text}");
        Page.PageError += (_, msg) => Console.WriteLine($"BROWSER ERROR: {msg}");
        
        // Create station
        await Page.ClickAsync("#profileDropdown");
        await Page.ClickAsync("text=\"Stations\"");
        await Page.ClickAsync("text=Create New Station");
        await Page.FillAsync("input[name='Name']", "Nav Test Station");
        await Page.FillAsync("input[name='Feeds']", firstFeed);
        await Page.ClickAsync("input[type='submit']");
        
        await Page.WaitForURLAsync("**/Stations", new() { Timeout = 10000 });
        await Page.GotoAsync(_factory.ServerAddress);
        Page.Console += (_, msg) => Console.WriteLine($"BROWSER CONSOLE: {msg.Text}");
        Page.PageError += (_, msg) => Console.WriteLine($"BROWSER ERROR: {msg}");

        var stationCard = Page.Locator(".card-station", new PageLocatorOptions { HasTextString = "Nav Test Station" }).First;
        await stationCard.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        // Ensure the dropdown works initially
        var dropdown = Page.Locator("#profileDropdown");
        await Expect(dropdown).ToBeVisibleAsync();
        
        // Verify dropdown menu is NOT visible
        await Expect(Page.Locator(".dropdown-menu.show")).ToHaveCountAsync(0);
        
        // Click to open
        await dropdown.ClickAsync();
        await Expect(Page.Locator(".dropdown-menu.show")).ToBeVisibleAsync();
        
        // Click again to close (or click outside)
        await dropdown.ClickAsync();
        await Expect(Page.Locator(".dropdown-menu.show")).ToHaveCountAsync(0);

        // Store a reference to the dropdown DOM element to see if it changes
        await Page.EvaluateAsync(@"() => {
            window.__myDropdown = document.getElementById('profileDropdown');
        }");

        // Click the gear icon
        await stationCard.Locator("a[href^='/Stations/Edit/']").ClickAsync();
        await Expect(Page.Locator("h1")).ToContainTextAsync("Edit Station", new() { Timeout = 10000 });

        // Navigate back using browser back
        await Page.GoBackAsync();
        
        // Wait for dashboard to reload
        await Expect(Page.Locator("h2")).ToContainTextAsync("Listen", new() { Timeout = 10000 });
        await Task.Delay(1000); // Give the browser a moment to settle after popstate

        // Try to click dropdown again
        await dropdown.ClickAsync();
        
        var showCount = await Page.Locator(".dropdown-menu.show").CountAsync();
        if (showCount == 0) 
        {
            var htmxBoost = await Page.EvaluateAsync<string>("() => document.body.getAttribute('hx-boost')");
            Console.WriteLine($"HTMX BOOST ON BODY: {htmxBoost}");
            
            var headerCount = await Page.Locator("header").CountAsync();
            Console.WriteLine($"HEADER COUNT: {headerCount}");
            
            var typeOfDropdown = await Page.EvaluateAsync<string>("() => typeof window.__myDropdown");
            Console.WriteLine($"TYPEOF STORED DROPDOWN: {typeOfDropdown}");
            var isSame = await Page.EvaluateAsync<bool>("() => window.__myDropdown === document.getElementById('profileDropdown')");
            Console.WriteLine($"IS SAME DROPDOWN ELEMENT: {isSame}");
            
            // Add a manual listener to see if click is firing
            await Page.EvaluateAsync(@"() => {
                document.getElementById('profileDropdown').addEventListener('click', (e) => {
                    console.log('Dropdown clicked!', e.defaultPrevented, 'Bootstrap is:', typeof bootstrap);
                });
            }");
            await dropdown.ClickAsync();
            await Task.Delay(500);
        }

        // If the bug exists, this will fail because .show class won't be added
        await Expect(Page.Locator(".dropdown-menu.show")).ToBeVisibleAsync(new() { Timeout = 3000 });
    }

    [Test]
    public async Task AuthenticatedUser_HasNoFooter_AndHasLegalAboutGitHubInDropdown()
    {
        // Go to home page (authenticated by Setup)
        await Page.GotoAsync(_factory.ServerAddress);

        // Footer should be hidden
        await Expect(Page.Locator("footer")).ToHaveCountAsync(0);

        // Click dropdown to open
        var dropdown = Page.Locator("#profileDropdown");
        await Expect(dropdown).ToBeVisibleAsync();
        await dropdown.ClickAsync();

        // Check for menu items
        await Expect(Page.Locator("text=About")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=GitHub")).ToBeVisibleAsync();
    }
}
