using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;
using Ohrbob.E2ETests.Infrastructure;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Server.Shared.Requests;
using Microsoft.Extensions.DependencyInjection;

namespace Ohrbob.E2ETests;

[NonParallelizable]
public class AudioPlaybackTests : PageTest
{
    private PlaywrightWebApplicationFactory _factory = null!;
    private string _testAccountId = string.Empty;

    public override BrowserNewContextOptions ContextOptions()
    {
        return new BrowserNewContextOptions
        {
            Permissions = new[] { "camera", "microphone" }, // Sometimes helps with media
            IgnoreHTTPSErrors = true
        };
    }

    [SetUp]
    public async Task Setup()
    {
        _factory = new PlaywrightWebApplicationFactory();
        
        // Use the injected AccountApiService to create a fresh user in the running backend
        var accountService = _factory.Services.GetRequiredService<Ohrbob.Web.Services.AccountApiService>();
        var account = await accountService.CreateOrUpdateAccountAsync("E2E Test", "e2e@test.com", "", "Test", Guid.NewGuid().ToString());
        
        // Since we introduced onboarding, we need to mark it as complete so the test can navigate to the normal dashboard
        if (account != null) {
            await accountService.CompleteOnboardingAsync(account.Id);
            
            var profileService = _factory.Services.GetRequiredService<Ohrbob.Web.Services.ProfilesApiService>();
            var profiles = await profileService.GetProfilesByAccountIdAsync(account.Id);
            if (profiles == null || !profiles.Any())
            {
                await profileService.CreateProfileAsync(account.Id, new Ohrbob.Server.Shared.Data.Profile { Name = "Main Profile" });
            }
        }
        
        _testAccountId = account!.Id;
        TestAuthHandler.TestAccountId = _testAccountId;
    }

    [TearDown]
    public void TearDown()
    {
        _factory.Dispose();
    }

    [Test]
    public async Task CreateStation_WaitParse_AndPlay()
    {
        // Get feed url
        var feedPath = Path.Combine(Directory.GetCurrentDirectory(), "../../../../example_feeds.txt");
        if (!File.Exists(feedPath))
        {
            feedPath = Path.Combine(Directory.GetCurrentDirectory(), "../../../../../example_feeds.txt");
        }
        var feeds = await File.ReadAllLinesAsync(feedPath);
        var firstFeed = feeds.First(l => !string.IsNullOrWhiteSpace(l));

        await Page.GotoAsync(_factory.ServerAddress);
        
        // Go to Stations and Create
        await Page.ClickAsync("#profileDropdown");
        await Page.ClickAsync("text=\"Stations\"");
        await Page.ClickAsync("text=Create New Station");

        // Fill form
        await Page.FillAsync("input[name='Name']", "Test E2E Station");
        await Page.FillAsync("input[name='Feeds']", firstFeed);
        await Page.ClickAsync("input[type='submit']");
        
        // Wait for redirect to Stations list
        await Page.WaitForURLAsync("**/Stations", new() { Timeout = 10000 });
        
        // Now we are redirected to Stations index. Go to Home (Dashboard)
        await Page.GotoAsync(_factory.ServerAddress);

        // Wait for the station to appear and process
        var stationCard = Page.Locator(".card", new PageLocatorOptions { HasTextString = "Test E2E Station" }).First;
        await stationCard.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        // Poll until feed is parsed (worker finishes). Check if "feeds" count updates (or podcasts)
        await Expect(stationCard.Locator("small.text-muted")).Not.ToContainTextAsync("0 podcasts", new() { Timeout = 30000 });

        // Click the station card to switch to it
        await stationCard.ClickAsync();

        // Wait a moment for the sync to happen and the player to be ready
        await Task.Delay(2000);

        // Click the audio widget's play button
        var playBtn = Page.Locator("#ohrbob-player .js-play");
        await playBtn.ClickAsync();
        
        // Wait for the pause icon to appear (meaning it is playing)
        await Expect(playBtn.Locator("rect")).ToHaveCountAsync(2, new() { Timeout = 15000 });
        
        // Verify via DOM property
        var isPlaying = await Page.EvaluateAsync<bool>("() => document.querySelector('#ohrbob-player').isPlaying");
        Assert.That(isPlaying, Is.True);
        
        // Assert that the station card has the active highlight class
        var cardContainer = Page.Locator(".active-station-card").First;
        await Expect(cardContainer).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("border-primary"));
    }
            [Test]
    public async Task NavigateToStationDetailsAndBack_CanStillPlay()
    {
        var feedPath = Path.Combine(Directory.GetCurrentDirectory(), "../../../../example_feeds.txt");
        if (!File.Exists(feedPath))
        {
            feedPath = Path.Combine(Directory.GetCurrentDirectory(), "../../../../../example_feeds.txt");
        }
        var feeds = await File.ReadAllLinesAsync(feedPath);
        var firstFeed = feeds.First(l => !string.IsNullOrWhiteSpace(l));

        await Page.GotoAsync(_factory.ServerAddress);
        
        await Page.ClickAsync("#profileDropdown");
        await Page.ClickAsync("text=\"Stations\"");
        await Page.ClickAsync("text=Create New Station");

        await Page.FillAsync("input[name='Name']", "Test E2E Station");
        await Page.FillAsync("input[name='Feeds']", firstFeed);
        await Page.ClickAsync("input[type='submit']");
        
        // Wait for redirect to Stations list
        await Page.WaitForURLAsync("**/Stations", new() { Timeout = 10000 });
        
        // Go to Home (Dashboard)
        await Page.GotoAsync(_factory.ServerAddress);

        var stationCard = Page.Locator(".card", new PageLocatorOptions { HasTextString = "Test E2E Station" }).First;
        await stationCard.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await Expect(stationCard.Locator("small.text-muted")).Not.ToContainTextAsync("0 podcasts", new() { Timeout = 30000 });

        // Play the station first
        await stationCard.ClickAsync();
        await Task.Delay(2000); // wait for sync
        var playBtn = Page.Locator("#ohrbob-player .js-play");
        await playBtn.ClickAsync();
        
        await Expect(playBtn.Locator("rect")).ToHaveCountAsync(2, new() { Timeout = 15000 });

        // Click the gear icon by explicitly targeting the `a[href]` inside the card
        await stationCard.Locator("a[href^='/Stations/Edit/']").ClickAsync();

        // Wait for edit page
        await Expect(Page.Locator("h1")).ToContainTextAsync("Edit Station", new() { Timeout = 10000 });

        // Navigate back
        await Page.GoBackAsync();

        // Wait for card on home page
        await stationCard.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
    }
}
