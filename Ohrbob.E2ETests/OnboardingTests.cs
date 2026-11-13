using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;
using Ohrbob.E2ETests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Ohrbob.E2ETests;

[NonParallelizable]
public class OnboardingTests : PageTest
{
    private PlaywrightWebApplicationFactory _factory = null!;
    private string _testAccountId = string.Empty;

    public override BrowserNewContextOptions ContextOptions()
    {
        return new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true
        };
    }

    [SetUp]
    public async Task Setup()
    {
        _factory = new PlaywrightWebApplicationFactory();
        
        // Use the injected AccountApiService to create a fresh user in the running backend
        var accountService = _factory.Services.GetRequiredService<Ohrbob.Web.Services.AccountApiService>();
        var uniqueEmail = $"onboard-{Guid.NewGuid()}@test.com";
        var account = await accountService.CreateOrUpdateAccountAsync("E2E Onboarding Test", uniqueEmail, "", "Test", Guid.NewGuid().ToString());
        
        _testAccountId = account!.Id;
        TestAuthHandler.TestAccountId = _testAccountId;
    }

    [TearDown]
    public void TearDown()
    {
        _factory.Dispose();
    }

    [Test]
    public async Task CompleteOnboarding_CreatesProfileAndStation()
    {
        Page.Request += (_, request) => Console.WriteLine($"[Playwright Request] {request.Method} {request.Url}");
        Page.Response += (_, response) => Console.WriteLine($"[Playwright Response] {response.Status} {response.Url}");
        Page.Console += (_, msg) => Console.WriteLine($"BROWSER CONSOLE: {msg.Text}");
        Page.PageError += (_, msg) => Console.WriteLine($"BROWSER ERROR: {msg}");

        // Go to root, should redirect to /Onboarding
        await Page.GotoAsync(_factory.ServerAddress);

        // We should be on the Onboarding Welcome page
        await Expect(Page.Locator("h1")).ToContainTextAsync("Welcome to Ohrbob!");

        // Step 1: Create Profile
        await Page.FillAsync("input[name='profileName']", "My E2E Profile");
        await Page.CheckAsync("input[name='acceptTerms']");
        await Page.ClickAsync("button[type='submit']");

        // Step 2: Create Station
        await Expect(Page.Locator("h2")).ToContainTextAsync("Create Your First Station");
        await Page.FillAsync("input[name='stationName']", "History");
        await Page.ClickAsync("button[type='submit']");

        // Step 3: Add Feeds
        await Expect(Page.Locator("h2")).ToContainTextAsync("Fill \"History\"");

        // The page auto-searches based on station name. Wait for results to appear.
        // We look for the first add feed button.
        var addFeedBtn = Page.Locator(".add-feed-btn").First;
        await addFeedBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        
        // Click the first feed to add it
        await addFeedBtn.ClickAsync();

        // The finish bar should now be visible and we can submit
        var finishBtn = Page.Locator("#finish-form button[type='submit']");
        await Expect(finishBtn).ToBeVisibleAsync();
        await finishBtn.ClickAsync();

        // After finish, we should end up on Home/Index which displays "Listen"
        await Expect(Page.Locator("h2")).ToContainTextAsync("Listen", new() { Timeout = 15000 });

        // Assert Profile is selected
        var profileDropdown = Page.Locator("#profileDropdown");
        await Expect(profileDropdown).ToHaveAttributeAsync("title", "My E2E Profile");

        // Assert Station is displayed
        var stationCard = Page.Locator(".card-station-title");
        await Expect(stationCard).ToContainTextAsync("History");
    }

    [Test]
    public async Task AbortAndRetryOnboarding_DoesNotCreateDuplicateProfile()
    {
        // Fresh account -> root redirects to the onboarding welcome page.
        await Page.GotoAsync(_factory.ServerAddress);
        await Expect(Page.Locator("h1")).ToContainTextAsync("Welcome to Ohrbob!");

        // Step 1, first attempt: create the profile, then abandon the flow.
        await Page.FillAsync("input[name='profileName']", "Retry Profile");
        await Page.CheckAsync("input[name='acceptTerms']");
        await Page.ClickAsync("button[type='submit']");
        await Expect(Page.Locator("h2")).ToContainTextAsync("Create Your First Station");

        // Abort: go back to the onboarding start (account still needs onboarding).
        await Page.GotoAsync($"{_factory.ServerAddress}/Onboarding");
        await Expect(Page.Locator("h1")).ToContainTextAsync("Welcome to Ohrbob!");

        // Retry step 1 with a different name; the existing profile must be reused.
        await Page.FillAsync("input[name='profileName']", "Retry Profile Again");
        await Page.CheckAsync("input[name='acceptTerms']");
        await Page.ClickAsync("button[type='submit']");
        await Expect(Page.Locator("h2")).ToContainTextAsync("Create Your First Station");

        var profilesService = _factory.Services.GetRequiredService<Ohrbob.Web.Services.ProfilesApiService>();
        var profiles = await profilesService.GetProfilesByAccountIdAsync(_testAccountId);
        Assert.That(profiles, Has.Count.EqualTo(1), "Retrying onboarding must not create duplicate profiles.");
    }
}
