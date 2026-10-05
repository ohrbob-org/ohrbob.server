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
        
        // Go to Manage Stations and Create
        await Page.ClickAsync("#profileDropdown");
        await Page.ClickAsync("text=Manage Stations");
        await Page.ClickAsync("text=Create New Station");

        // Fill form
        await Page.FillAsync("input[name='Name']", "Test E2E Station");
        await Page.FillAsync("input[name='Feeds']", firstFeed);
        await Page.ClickAsync("input[type='submit']");

        // Now we are redirected to Stations index. Go to Home (Dashboard)
        await Page.ClickAsync(".floating-nav-pill a");

        // Wait for the station to appear and process
        var stationCard = Page.Locator(".card-body:has-text('Test E2E Station')");
        await stationCard.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        // Poll until feed is parsed (worker finishes). Check if "feeds" count updates (or podcasts)
        await Expect(stationCard.Locator("small.text-muted")).Not.ToContainTextAsync("0 podcasts", new() { Timeout = 30000 });

        // Click the station card to switch to it
        await Page.Locator(".card:has-text('Test E2E Station')").ClickAsync();

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
}