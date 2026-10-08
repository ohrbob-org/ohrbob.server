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
        Page.Console += (_, msg) => Console.WriteLine($"BROWSER CONSOLE: {msg.Text}");
        Page.PageError += (_, msg) => Console.WriteLine($"BROWSER ERROR: {msg}");
        
        // Mock HTMLMediaElement audio playback for determinism and speed by redirecting to a silent local WAV URI
        await Page.AddInitScriptAsync(@"
            (() => {
                const desc = Object.getOwnPropertyDescriptor(HTMLMediaElement.prototype, 'src');
                if (desc && desc.set) {
                    Object.defineProperty(HTMLMediaElement.prototype, 'src', {
                        get: function() {
                            return desc.get.call(this);
                        },
                        set: function(val) {
                            if (val && val.startsWith('http') && !val.includes('base64')) {
                                desc.set.call(this, 'data:audio/wav;base64,UklGRigAAABXQVZFZm10IBAAAAABAAEARKwAAIhYAQACABAAZGF0YQQAAAAAAAAGAA==');
                            } else {
                                desc.set.call(this, val);
                            }
                        }
                    });
                }
            })();
        ");
        
        // Use the injected AccountApiService to create a fresh user in the running backend
        var accountService = _factory.Services.GetRequiredService<Ohrbob.Web.Services.AccountApiService>();
        var uniqueEmail = $"e2e-{Guid.NewGuid()}@test.com";
        var account = await accountService.CreateOrUpdateAccountAsync("E2E Test", uniqueEmail, "", "Test", Guid.NewGuid().ToString());
        
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

        // Wait for the player to load the episode metadata (meaning it's not the placeholder "Track Name" anymore)
        await Expect(Page.Locator("#ohrbob-player .track-title")).Not.ToHaveTextAsync("Track Name", new() { Timeout = 15000 });

        // Click the audio widget's play button
        var playBtn = Page.Locator("#ohrbob-player .js-play");
        await playBtn.ClickAsync();
        
        // Wait for the pause icon to appear (meaning it is playing)
        await Expect(playBtn.Locator("rect")).ToHaveCountAsync(2, new() { Timeout = 45000 });
        
        // Verify via DOM property
        var isPlaying = await Page.EvaluateAsync<bool>("() => document.querySelector('#ohrbob-player').isPlaying");
        Assert.That(isPlaying, Is.True);
        
        // Assert that the station card has the active highlight class
        var cardContainer = Page.Locator(".active-station-card").First;
        await Expect(cardContainer).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("border-primary"));

        // Pause playback to release server resources
        await playBtn.ClickAsync();
        await Expect(playBtn.Locator("rect")).ToHaveCountAsync(0, new() { Timeout = 10000 });
    }
            [Test]
    public async Task ANavigateToStationDetailsAndBack_CanStillPlay()
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
        
        // Wait for the player to load the episode metadata (meaning it's not the placeholder "Track Name" anymore)
        await Expect(Page.Locator("#ohrbob-player .track-title")).Not.ToHaveTextAsync("Track Name", new() { Timeout = 15000 });
        
        var playBtn = Page.Locator("#ohrbob-player .js-play");
        await playBtn.ClickAsync();
        
        try
        {
            await Expect(playBtn.Locator("rect")).ToHaveCountAsync(2, new() { Timeout = 45000 });
        }
        catch (Exception)
        {
            var html = await Page.EvaluateAsync<string>("() => document.getElementById('ohrbob-player').outerHTML");
            Console.WriteLine($"PLAYER HTML ON FAILURE: {html}");
            throw;
        }

        // Click the gear icon by explicitly targeting the `a[href]` inside the card
        await stationCard.Locator("a[href^='/Stations/Edit/']").ClickAsync();

        // Wait for edit page
        await Expect(Page.Locator("h1")).ToContainTextAsync("Edit Station", new() { Timeout = 10000 });

        // Navigate back
        await Page.GoBackAsync();

        // Wait for card on home page
        await stationCard.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        // Assert that the player is STILL playing and has the pause icon (2 rects)
        await Expect(playBtn.Locator("rect")).ToHaveCountAsync(2, new() { Timeout = 10000 });

        // Pause playback to release server resources
        await playBtn.ClickAsync();
        await Expect(playBtn.Locator("rect")).ToHaveCountAsync(0, new() { Timeout = 10000 });
    }
}
