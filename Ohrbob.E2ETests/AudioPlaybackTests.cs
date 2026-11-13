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
        await Context.ClearCookiesAsync();
        _factory = new PlaywrightWebApplicationFactory();
        Page.Console += (_, msg) => Console.WriteLine($"BROWSER CONSOLE: {msg.Text}");
        Page.PageError += (_, msg) => Console.WriteLine($"BROWSER ERROR: {msg}");
        
        // Mock HTMLMediaElement audio playback for complete determinism and speed
        await Page.AddInitScriptAsync(@"
            (() => {
                class MockAudio {
                    constructor() {
                        this._listeners = {};
                        this._src = '';
                        this._currentTime = 0;
                        this._duration = 300;
                        this._volume = 1;
                        this._muted = false;
                        this._paused = true;
                        this.readyState = 4;
                    }
                    addEventListener(event, callback) {
                        if (!this._listeners[event]) this._listeners[event] = [];
                        this._listeners[event].push(callback);
                    }
                    removeEventListener(event, callback) {
                        if (!this._listeners[event]) return;
                        this._listeners[event] = this._listeners[event].filter(cb => cb !== callback);
                    }
                    dispatchEvent(event) {
                        const eventType = event.type;
                        if (this._listeners[eventType]) {
                            this._listeners[eventType].forEach(cb => cb(event));
                        }
                    }
                    get src() { return this._src; }
                    set src(val) { this._src = val; }
                    get currentTime() { return this._currentTime; }
                    set currentTime(val) { this._currentTime = val; }
                    get duration() { return this._duration; }
                    set duration(val) { this._duration = val; }
                    get volume() { return this._volume; }
                    set volume(val) { this._volume = val; }
                    get muted() { return this._muted; }
                    set muted(val) { this._muted = val; }
                    get paused() { return this._paused; }
                    
                    play() {
                        console.log('MOCK AUDIO play() called! Stacktrace:', new Error().stack);
                        this._paused = false;
                        setTimeout(() => {
                            this.dispatchEvent(new Event('play'));
                            this.dispatchEvent(new Event('playing'));
                        }, 10);
                        return Promise.resolve();
                    }
                    pause() {
                        console.log('MOCK AUDIO pause() called! Stacktrace:', new Error().stack);
                        this._paused = true;
                        setTimeout(() => {
                            this.dispatchEvent(new Event('pause'));
                        }, 10);
                    }
                    load() {}
                }
                window.Audio = MockAudio;

                if (!('mediaSession' in navigator)) {
                    navigator.mediaSession = {
                        metadata: null,
                        playbackState: 'none',
                        setActionHandler: function(action, callback) {
                            if (!this._handlers) this._handlers = {};
                            this._handlers[action] = callback;
                        },
                        setPositionState: function(state) {
                            this._positionState = state;
                        }
                    };
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
    public async Task TearDown()
    {
        await Context.CloseAsync();
        _factory.Dispose();
        TestAuthHandler.TestAccountId = string.Empty;
    }

    [Test]
    public async Task StationLink_PointsToStationEditView()
    {
        var feedPath = Path.Combine(Directory.GetCurrentDirectory(), "../../../../example_feeds.txt");
        if (!File.Exists(feedPath))
        {
            feedPath = Path.Combine(Directory.GetCurrentDirectory(), "../../../../../example_feeds.txt");
        }
        var feeds = await File.ReadAllLinesAsync(feedPath);
        var firstFeed = feeds.First(l => !string.IsNullOrWhiteSpace(l));

        await Page.GotoAsync(_factory.ServerAddress);

        // Create a station
        await Page.ClickAsync("#profileDropdown");
        await Page.ClickAsync("text=\"Stations\"");
        await Page.ClickAsync("text=Create New Station");
        await Page.FillAsync("input[name='Name']", "Link Target Station");
        await Page.FillAsync("input[name='Feeds']", firstFeed);
        await Page.ClickAsync("input[type='submit']");
        await Page.WaitForURLAsync("**/Stations", new() { Timeout = 10000 });

        // Go to Home (Dashboard) and wait for the feed to be parsed
        await Page.GotoAsync(_factory.ServerAddress);
        var stationCard = Page.Locator(".card-station", new PageLocatorOptions { HasTextString = "Link Target Station" }).First;
        await stationCard.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await Expect(stationCard.Locator("small.text-muted")).Not.ToContainTextAsync("0 podcasts", new() { Timeout = 30000 });

        // The expected target: the station's edit view, as linked by the card's edit gear
        var editHref = await stationCard.Locator("a[title='Manage Station']").GetAttributeAsync("href");
        Assert.That(editHref, Is.Not.Null.And.Not.Empty);

        // Activate the station so the player shows its metadata
        await stationCard.Locator(".card-station-play").ClickAsync();
        await Expect(Page.Locator("#ohrbob-player .expanded-title")).Not.ToHaveTextAsync("Track Name", new() { Timeout = 15000 });

        // The widget's station link must point to the station edit view as a full absolute URL
        // (built by Razor on the server, not concatenated in JavaScript)
        var stationLink = Page.Locator("#ohrbob-player .audio-bar .station-link-icon");
        await Expect(stationLink).ToBeVisibleAsync(new() { Timeout = 10000 });
        var href = await stationLink.GetAttributeAsync("href");
        Assert.That(href, Is.Not.Null.And.Not.Empty);
        Assert.That(href!.StartsWith("http"), Is.True, $"Expected absolute URL but was '{href}'");
        Assert.That(href.EndsWith(editHref!), Is.True, $"Expected '{href}' to end with '{editHref}'");

        // It must stay in-tab so htmx boost swaps #main-content without interrupting playback
        var target = await stationLink.GetAttributeAsync("target");
        Assert.That(target, Is.Null.Or.Empty);
    }

    [Test]
    public async Task Pause_DisablesAutoplay_UntilPlayPressedAgain()
    {
        var feedPath = Path.Combine(Directory.GetCurrentDirectory(), "../../../../example_feeds.txt");
        if (!File.Exists(feedPath))
        {
            feedPath = Path.Combine(Directory.GetCurrentDirectory(), "../../../../../example_feeds.txt");
        }
        var feeds = await File.ReadAllLinesAsync(feedPath);
        var firstFeed = feeds.First(l => !string.IsNullOrWhiteSpace(l));

        await Page.GotoAsync(_factory.ServerAddress);

        // Create a station
        await Page.ClickAsync("#profileDropdown");
        await Page.ClickAsync("text=\"Stations\"");
        await Page.ClickAsync("text=Create New Station");
        await Page.FillAsync("input[name='Name']", "Pause State Station");
        await Page.FillAsync("input[name='Feeds']", firstFeed);
        await Page.ClickAsync("input[type='submit']");
        await Page.WaitForURLAsync("**/Stations", new() { Timeout = 10000 });

        // Go to Home (Dashboard) and wait for the feed to be parsed
        await Page.GotoAsync(_factory.ServerAddress);
        var stationCard = Page.Locator(".card-station", new PageLocatorOptions { HasTextString = "Pause State Station" }).First;
        await stationCard.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await Expect(stationCard.Locator("small.text-muted")).Not.ToContainTextAsync("0 podcasts", new() { Timeout = 30000 });

        // Start playback via the station card (explicit user action)
        await stationCard.Locator(".card-station-play").ClickAsync();
        var playBtn = Page.Locator("#ohrbob-player .js-play");
        await Expect(playBtn.Locator("rect")).ToHaveCountAsync(2, new() { Timeout = 45000 }); // pause icon = playing

        // Press pause -> the "user paused" state must be stored in a cookie
        await playBtn.ClickAsync();
        await Expect(playBtn.Locator("rect")).ToHaveCountAsync(0, new() { Timeout = 10000 }); // play icon = paused

        var cookies = await Page.Context.CookiesAsync();
        Assert.That(cookies.Any(c => c.Name == "ohrbob_user_paused" && c.Value == "1"), Is.True,
            "Pausing must store the paused state in the ohrbob_user_paused cookie");

        // Reload the page -> playback must NOT automatically continue
        await Page.ReloadAsync();
        await Expect(Page.Locator("#ohrbob-player .expanded-title")).Not.ToHaveTextAsync("Track Name", new() { Timeout = 15000 });
        await Page.WaitForTimeoutAsync(1500); // give any (unwanted) autoplay a chance to happen
        var isPlayingAfterReload = await Page.EvaluateAsync<bool>("() => document.querySelector('#ohrbob-player').isPlaying");
        Assert.That(isPlayingAfterReload, Is.False, "Playback must not auto-continue after the user paused");
        var playBtnAfterReload = Page.Locator("#ohrbob-player .js-play");
        await Expect(playBtnAfterReload.Locator("rect")).ToHaveCountAsync(0);

        // Press play again -> state resets to auto-play and playback starts
        await playBtnAfterReload.ClickAsync();
        await Expect(playBtnAfterReload.Locator("rect")).ToHaveCountAsync(2, new() { Timeout = 10000 });

        cookies = await Page.Context.CookiesAsync();
        Assert.That(cookies.Any(c => c.Name == "ohrbob_user_paused" && c.Value == "1"), Is.False,
            "Pressing play must clear the paused state cookie");

        // Reload once more -> auto-play is restored
        await Page.ReloadAsync();
        await Expect(Page.Locator("#ohrbob-player .expanded-title")).Not.ToHaveTextAsync("Track Name", new() { Timeout = 15000 });
        var playBtnFinal = Page.Locator("#ohrbob-player .js-play");
        await Expect(playBtnFinal.Locator("rect")).ToHaveCountAsync(2, new() { Timeout = 15000 });
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
        var stationCard = Page.Locator(".card-station", new PageLocatorOptions { HasTextString = "Test E2E Station" }).First;
        await stationCard.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        // Poll until feed is parsed (worker finishes). Check if "feeds" count updates (or podcasts)
        await Expect(stationCard.Locator("small.text-muted")).Not.ToContainTextAsync("0 podcasts", new() { Timeout = 30000 });

        // Click the station card to switch to it
        await stationCard.Locator(".card-station-play").ClickAsync();

        // Wait for the player to load the episode metadata (meaning it's not the placeholder "Track Name" anymore)
        await Expect(Page.Locator("#ohrbob-player .expanded-title")).Not.ToHaveTextAsync("Track Name", new() { Timeout = 15000 });

        // If the player is not already playing (which it should be after clicking the card, but let's be safe), click play
        var playBtn = Page.Locator("#ohrbob-player .js-play");
        var rectCount = await playBtn.Locator("rect").CountAsync();
        if (rectCount == 0)
        {
            await playBtn.ClickAsync();
        }
        
        // Wait for the pause icon to appear (meaning it is playing)
        await Expect(playBtn.Locator("rect")).ToHaveCountAsync(2, new() { Timeout = 45000 });
        
        // Verify via DOM property
        var isPlaying = await Page.EvaluateAsync<bool>("() => document.querySelector('#ohrbob-player').isPlaying");
        Assert.That(isPlaying, Is.True);
        
        // Assert that the station card has the active highlight class
        var cardContainer = Page.Locator(".active-station-card").First;
        await Expect(cardContainer).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bis-active\b"));

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

        var stationCard = Page.Locator(".card-station", new PageLocatorOptions { HasTextString = "Test E2E Station" }).First;
        await stationCard.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await Expect(stationCard.Locator("small.text-muted")).Not.ToContainTextAsync("0 podcasts", new() { Timeout = 30000 });

        // Play the station first
        await stationCard.Locator(".card-station-play").ClickAsync();
        
        // Wait for the player to load the episode metadata (meaning it's not the placeholder "Track Name" anymore)
        await Expect(Page.Locator("#ohrbob-player .expanded-title")).Not.ToHaveTextAsync("Track Name", new() { Timeout = 15000 });
        
        var playBtn = Page.Locator("#ohrbob-player .js-play");
        var rectCount = await playBtn.Locator("rect").CountAsync();
        if (rectCount == 0)
        {
            await playBtn.ClickAsync();
        }
        
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
        await stationCard.Locator("a[title='Manage Station']").ClickAsync();

        // Wait for edit page
        await Expect(Page.Locator("h1 #stationNameDisplay")).ToBeVisibleAsync(new() { Timeout = 10000 });

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

    [Test]
    public async Task NavigateThroughHtmxBoostedEndpoints_KeepAudioPlaying()
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

        var stationCard = Page.Locator(".card-station", new PageLocatorOptions { HasTextString = "Test E2E Station" }).First;
        await stationCard.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await Expect(stationCard.Locator("small.text-muted")).Not.ToContainTextAsync("0 podcasts", new() { Timeout = 30000 });

        // Play the station first
        await stationCard.Locator(".card-station-play").ClickAsync();
        
        // Wait for the player to load the episode metadata (meaning it's not the placeholder "Track Name" anymore)
        await Expect(Page.Locator("#ohrbob-player .expanded-title")).Not.ToHaveTextAsync("Track Name", new() { Timeout = 15000 });
        
        var playBtn = Page.Locator("#ohrbob-player .js-play");
        var rectCount = await playBtn.Locator("rect").CountAsync();
        if (rectCount == 0)
        {
            await playBtn.ClickAsync();
        }
        
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

        // Click the "Discover" link in the header
        await Page.ClickAsync(".floating-nav-pill-center a:has-text(\"Discover\")");

        // Wait for the URL to change to /Discover
        await Page.WaitForURLAsync("**/Discover", new() { Timeout = 10000 });

        // Verify that the player is still playing (assert the pause button has 2 rects, or check isPlaying is true via page evaluation)
        await Expect(playBtn.Locator("rect")).ToHaveCountAsync(2, new() { Timeout = 10000 });
        var isPlaying = await Page.EvaluateAsync<bool>("() => document.querySelector('#ohrbob-player').isPlaying");
        Assert.That(isPlaying, Is.True);

        // Click the "Listen" link in the header
        await Page.ClickAsync(".floating-nav-pill-center a:has-text(\"Listen\")");

        // Verify that the player is still playing
        await Expect(playBtn.Locator("rect")).ToHaveCountAsync(2, new() { Timeout = 10000 });
        isPlaying = await Page.EvaluateAsync<bool>("() => document.querySelector('#ohrbob-player').isPlaying");
        Assert.That(isPlaying, Is.True);

        // Pause playback to release server resources
        await playBtn.ClickAsync();
        await Expect(playBtn.Locator("rect")).ToHaveCountAsync(0, new() { Timeout = 10000 });
    }

    [Test]
    public async Task Playback_UpdatesMediaSessionMetadata()
    {
        var feedPath = Path.Combine(Directory.GetCurrentDirectory(), "../../../../example_feeds.txt");
        if (!File.Exists(feedPath))
        {
            feedPath = Path.Combine(Directory.GetCurrentDirectory(), "../../../../../example_feeds.txt");
        }
        var feeds = await File.ReadAllLinesAsync(feedPath);
        var firstFeed = feeds.First(l => !string.IsNullOrWhiteSpace(l));

        var stationName = $"Test E2E Station {Guid.NewGuid().ToString()[..8]}";

        await Page.GotoAsync(_factory.ServerAddress);
        
        await Page.ClickAsync("#profileDropdown");
        await Page.ClickAsync("text=\"Stations\"");
        await Page.ClickAsync("text=Create New Station");

        await Page.FillAsync("input[name='Name']", stationName);
        await Page.FillAsync("input[name='Feeds']", firstFeed);
        await Page.ClickAsync("input[type='submit']");
        
        await Page.WaitForURLAsync("**/Stations", new() { Timeout = 10000 });
        
        await Page.GotoAsync(_factory.ServerAddress);

        var stationCard = Page.Locator(".card-station", new PageLocatorOptions { HasTextString = stationName }).First;
        await stationCard.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await Expect(stationCard.Locator("small.text-muted")).Not.ToContainTextAsync("0 podcasts", new() { Timeout = 30000 });

        await stationCard.Locator(".card-station-play").ClickAsync();
        
        await Expect(Page.Locator("#ohrbob-player .expanded-title")).Not.ToHaveTextAsync("Track Name", new() { Timeout = 15000 });
        
        var playBtn = Page.Locator("#ohrbob-player .js-play");
        var rectCount = await playBtn.Locator("rect").CountAsync();
        if (rectCount == 0)
        {
            await playBtn.ClickAsync();
        }
        
        await Expect(playBtn.Locator("rect")).ToHaveCountAsync(2, new() { Timeout = 45000 });

        var metadata = await Page.EvaluateAsync<JsonElement>(@"() => {
            if (!navigator.mediaSession || !navigator.mediaSession.metadata) return null;
            return {
                title: navigator.mediaSession.metadata.title,
                artist: navigator.mediaSession.metadata.artist,
                album: navigator.mediaSession.metadata.album
            };
        }");
        
        Assert.That(metadata.ValueKind, Is.Not.EqualTo(JsonValueKind.Null));
        var title = metadata.GetProperty("title").GetString();
        var artist = metadata.GetProperty("artist").GetString();
        Assert.That(title, Is.Not.Null.And.Not.Empty);
        Assert.That(artist, Is.EqualTo(stationName));

        await playBtn.ClickAsync();
    }
}
