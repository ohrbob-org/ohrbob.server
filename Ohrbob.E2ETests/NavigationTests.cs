using Microsoft.Playwright;
using MongoDB.Driver;
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
        await Expect(Page.Locator("h1 #stationNameDisplay")).ToBeVisibleAsync(new() { Timeout = 10000 });

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

    [Test]
    public async Task BoostedNavigation_DoesNotScrollThePageDown()
    {
        if (!await TestCasts.IsAvailableAsync())
        {
            Assert.Ignore($"podcast-testcasts is not reachable at {TestCasts.ConfigureBaseUrl}.");
        }

        var slug = $"navscroll-{Guid.NewGuid():N}";
        await TestCasts.ConfigureAsync(
            new TestCastPodcast(slug, "Nav Scroll A", new[] { new TestCastEpisode("E1", $"{slug}-1.mp3") }),
            new TestCastPodcast($"{slug}-b", "Nav Scroll B", new[] { new TestCastEpisode("E2", $"{slug}-b-1.mp3") }));

        var stations = _factory.Services.GetRequiredService<Ohrbob.Web.Services.StationsApiService>();
        var profiles = _factory.Services.GetRequiredService<Ohrbob.Web.Services.ProfilesApiService>();
        var profileId = (await profiles.GetProfilesByAccountIdAsync(_testAccountId))![0].Id;
        foreach (var (name, feed) in new[] { ("Nav Scroll A", slug), ("Nav Scroll B", $"{slug}-b") })
        {
            var station = await stations.CreateStationAsync(_testAccountId, new Ohrbob.Server.Shared.Data.Station
            {
                AccountId = _testAccountId,
                Name = name,
                Feeds = new[] { TestCasts.FeedUrl(feed) }
            });
            await profiles.AddStationToProfileAsync(profileId, station!.Id);
        }

        // A Discover card so that page is scrollable too.
        var comedyFeed = new Ohrbob.Server.Shared.Data.Feed
        {
            Id = Guid.NewGuid().ToString(),
            Url = $"https://example.com/nav-scroll-{Guid.NewGuid()}.xml",
            Title = "Nav Scroll Show",
            Category = "Comedy",
            Description = "Scroll test.",
            CreatedAt = DateTime.UtcNow
        };
        var mongo = new MongoDB.Driver.MongoClient("mongodb://localhost:27019");
        var feeds = mongo.GetDatabase("OhrbobData").GetCollection<Ohrbob.Server.Shared.Data.Feed>("FeedCollection");
        await feeds.InsertOneAsync(comedyFeed);
        try
        {
            // A short viewport makes both pages scrollable.
            await Page.SetViewportSizeAsync(1280, 300);
            await Page.GotoAsync(_factory.ServerAddress);
            await Expect(Page.Locator(".card-station").First).ToBeVisibleAsync(new() { Timeout = 15000 });

            var discoverLink = Page.Locator(".floating-nav-pill-center a", new() { HasTextString = "Discover" });
            var listenLink = Page.Locator(".floating-nav-pill-center a", new() { HasTextString = "Listen" });

            // Listen -> Discover while scrolled down: the new page starts at the top.
            Assert.That(await Page.EvaluateAsync<int>("() => document.documentElement.scrollHeight"),
                Is.GreaterThan(await Page.EvaluateAsync<int>("() => window.innerHeight")),
                "Listen must be scrollable for this test");
            await Page.EvaluateAsync("() => window.scrollTo(0, 80)");
            Assert.That(await Page.EvaluateAsync<int>("() => window.scrollY"), Is.GreaterThan(0));
            await discoverLink.ClickAsync();
            await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/Discover"));
            await Expect(Page.Locator(".discover-lane .card-feed").First).ToBeVisibleAsync(new() { Timeout = 15000 });
            await Page.WaitForTimeoutAsync(300);
            Assert.That(await Page.EvaluateAsync<int>("() => window.scrollY"), Is.EqualTo(0),
                "Switching to Discover must start at the top");

            // Discover -> Listen while scrolled down: same.
            Assert.That(await Page.EvaluateAsync<int>("() => document.documentElement.scrollHeight"),
                Is.GreaterThan(await Page.EvaluateAsync<int>("() => window.innerHeight")),
                "Discover must be scrollable for this test");
            await Page.EvaluateAsync("() => window.scrollTo(0, 80)");
            Assert.That(await Page.EvaluateAsync<int>("() => window.scrollY"), Is.GreaterThan(0));
            await listenLink.ClickAsync();
            await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/$"));
            await Expect(Page.Locator(".card-station").First).ToBeVisibleAsync(new() { Timeout = 15000 });
            await Page.WaitForTimeoutAsync(300);
            Assert.That(await Page.EvaluateAsync<int>("() => window.scrollY"), Is.EqualTo(0),
                "Switching to Listen must start at the top");
        }
        finally
        {
            await feeds.DeleteOneAsync(f => f.Id == comedyFeed.Id);
        }
    }

    [Test]
    public async Task ActiveNav_HighlightsTheCurrentSectionAcrossBoostedNavigation()
    {
        await Page.SetViewportSizeAsync(1280, 900);
        await Page.GotoAsync(_factory.ServerAddress);

        var listen = Page.Locator(".floating-nav-pill-center a", new() { HasTextString = "Listen" });
        var discover = Page.Locator(".floating-nav-pill-center a", new() { HasTextString = "Discover" });
        var active = new System.Text.RegularExpressions.Regex(@"\bactive\b");

        await Expect(listen).ToHaveClassAsync(active);
        await Expect(listen).ToHaveAttributeAsync("aria-current", "page");
        await Expect(discover).Not.ToHaveClassAsync(active);

        // The header is outside #main-content, so the highlight must follow the
        // htmx-boosted navigation instead of relying on a new page render.
        await discover.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/Discover"));
        await Expect(discover).ToHaveClassAsync(active);
        await Expect(discover).ToHaveAttributeAsync("aria-current", "page");
        await Expect(listen).Not.ToHaveClassAsync(active);

        // Browser back restores the Listen highlight.
        await Page.GoBackAsync();
        await Expect(listen).ToHaveClassAsync(active);
        await Expect(discover).Not.ToHaveClassAsync(active);
    }
}
