using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;
using Ohrbob.E2ETests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Ohrbob.E2ETests;

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
        var account = await accountService.CreateOrUpdateAccountAsync("E2E Onboarding Test", "onboard@test.com", "", "Test", Guid.NewGuid().ToString());
        
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

        // Go to root, should redirect to /Onboarding
        await Page.GotoAsync(_factory.ServerAddress);

        // We should be on the Onboarding Welcome page
        await Expect(Page.Locator("h1")).ToContainTextAsync("Welcome to Ohrbob!");

        // Step 1: Create Profile
        await Page.FillAsync("input[name='profileName']", "My E2E Profile");
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
        await Expect(Page.Locator("h2")).ToContainTextAsync("Listen");

        // Assert Profile is selected
        var profileDropdown = Page.Locator("#profileDropdown");
        await Expect(profileDropdown).ToContainTextAsync("My E2E Profile");

        // Assert Station is displayed
        var stationCard = Page.Locator(".card-title");
        await Expect(stationCard).ToContainTextAsync("History");
    }
}
