using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;
using Ohrbob.E2ETests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Driver;
using Ohrbob.Server.Shared.Data;

[assembly: NonParallelizable]

namespace Ohrbob.E2ETests;

[NonParallelizable]
public class CompilingStationTests : PageTest
{
    private PlaywrightWebApplicationFactory _factory = null!;
    private string _testAccountId = string.Empty;

    [SetUp]
    public async Task Setup()
    {
        await Context.ClearCookiesAsync();
        _factory = new PlaywrightWebApplicationFactory();
        
        var accountService = _factory.Services.GetRequiredService<Ohrbob.Web.Services.AccountApiService>();
        var uniqueEmail = $"compiling-test-{Guid.NewGuid()}@test.com";
        var account = await accountService.CreateOrUpdateAccountAsync("E2E Compiling Test", uniqueEmail, "", "Test", Guid.NewGuid().ToString());
        
        if (account != null) {
            await accountService.CompleteOnboardingAsync(account.Id);
            var profileService = _factory.Services.GetRequiredService<Ohrbob.Web.Services.ProfilesApiService>();
            var profiles = await profileService.GetProfilesByAccountIdAsync(account.Id);
            if (profiles == null || !profiles.Any())
            {
                await profileService.CreateProfileAsync(account.Id, new Ohrbob.Server.Shared.Data.Profile { Name = "Compiling Profile" });
            }
        }
        
        _testAccountId = account!.Id;
        TestAuthHandler.TestAccountId = _testAccountId;
        
        // Seed a dummy compiling feed in MongoDB so ValidateAndEnsureFeedsExistAsync succeeds immediately but shows as compiling
        var client = new MongoClient("mongodb://localhost:27019");
        var database = client.GetDatabase("OhrbobData");
        var feedsCollection = database.GetCollection<Feed>("FeedCollection");
        var episodesCollection = database.GetCollection<Episode>("EpisodeCollection");
        
        // Delete any leftover compiling feed and episodes if exist
        await feedsCollection.DeleteManyAsync(f => f.Url == "https://example.com/compiling-test-feed.xml");
        await episodesCollection.DeleteManyAsync(e => e.FeedUrl == "https://example.com/compiling-test-feed.xml");
        
        var dummyFeed = new Feed
        {
            Id = "compiling-test-feed-id",
            Url = "https://example.com/compiling-test-feed.xml",
            Title = "Compiling Test Feed",
            Description = "A mock feed that is in compiling state",
            CreatedAt = DateTime.UtcNow,
            LastUpdated = null // Crucial: LastUpdated = null triggers isCompiling = true
        };
        await feedsCollection.InsertOneAsync(dummyFeed);

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
            })();
        ");
        
        _factory.CreateClient();
    }

    [TearDown]
    public async Task TearDown()
    {
        await Context.CloseAsync();
        _factory.Dispose();
        TestAuthHandler.TestAccountId = string.Empty;
        
        var client = new MongoClient("mongodb://localhost:27019");
        var database = client.GetDatabase("OhrbobData");
        var feedsCollection = database.GetCollection<Feed>("FeedCollection");
        var episodesCollection = database.GetCollection<Episode>("EpisodeCollection");
        await feedsCollection.DeleteManyAsync(f => f.Url == "https://example.com/compiling-test-feed.xml");
        await episodesCollection.DeleteManyAsync(e => e.FeedUrl == "https://example.com/compiling-test-feed.xml");
    }

    [Test]
    public async Task NewStation_ShowsCompiling_AndDisablesPlayback()
    {
        Page.Console += (_, msg) => Console.WriteLine($"BROWSER CONSOLE: {msg.Text}");
        Page.PageError += (_, err) => Console.WriteLine($"BROWSER PAGE ERROR: {err}");

        await Page.GotoAsync(_factory.ServerAddress);

        // Create a new station with a nonexistent fresh feed URL
        await Page.ClickAsync("#profileDropdown");
        await Page.ClickAsync("text=\"Stations\"");
        await Page.ClickAsync("text=Create New Station");
        await Page.FillAsync("input[name='Name']", "Fresh Station");
        await Page.FillAsync("input[name='Feeds']", "https://example.com/compiling-test-feed.xml");
        
        // Disable hx-boost for the submission
        var submitBtn = Page.Locator("input[type='submit']");
        await submitBtn.ClickAsync();

        await Page.WaitForURLAsync("**/Stations");
        await Page.GotoAsync(_factory.ServerAddress);

        var stationCard = Page.Locator(".card-station", new PageLocatorOptions { HasTextString = "Fresh Station" }).First;
        await stationCard.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        // Click the station card to switch to it
        await stationCard.ClickAsync();

        // Wait for the player to update and show compiling metadata
        var trackTitle = Page.Locator("#ohrbob-player .expanded-title");
        await Expect(trackTitle).ToHaveTextAsync("Compiling Station...", new() { Timeout = 10000 });

        // Verify the play button is disabled
        var playBtn = Page.Locator("#ohrbob-player .js-play");
        await Expect(playBtn).ToBeDisabledAsync();

        // Verify the skip back button is disabled
        var skipBackBtn = Page.Locator("#ohrbob-player .js-skipback");
        await Expect(skipBackBtn).ToBeDisabledAsync();
    }

    [Test]
    public async Task Discover_ShowsRecommendationLanes_AndHidesThemWhileSearching()
    {
        var feed = new Feed
        {
            Id = Guid.NewGuid().ToString(),
            Url = $"https://example.com/e2e-comedy-{Guid.NewGuid()}.xml",
            Title = "E2E Comedy Show",
            Category = "Comedy",
            CreatedAt = DateTime.UtcNow
        };

        var client = new MongoClient("mongodb://localhost:27019");
        var feedsCollection = client.GetDatabase("OhrbobData").GetCollection<Feed>("FeedCollection");
        await feedsCollection.InsertOneAsync(feed);

        try
        {
            await Page.GotoAsync($"{_factory.ServerAddress}/Discover");

            var lanes = Page.Locator("#recommendation-lanes");
            await Expect(lanes).ToBeVisibleAsync();

            var comedyLane = Page.Locator(".discover-lane", new PageLocatorOptions { HasTextString = "Comedy" }).First;
            await Expect(comedyLane).ToBeVisibleAsync();
            await Expect(comedyLane.Locator(".card-feed", new LocatorLocatorOptions { HasTextString = "E2E Comedy Show" })).ToBeVisibleAsync();

            await Page.FillAsync("#discover-search", "maus");
            await Expect(lanes).Not.ToBeVisibleAsync();

            await Page.FillAsync("#discover-search", "");
            await Expect(lanes).ToBeVisibleAsync();
        }
        finally
        {
            await feedsCollection.DeleteOneAsync(f => f.Id == feed.Id);
        }
    }

    [Test]
    public async Task Discover_ShowsPlaceholder_WhenPodcastHasNoCoverArt()
    {
        var feed = new Feed
        {
            Id = Guid.NewGuid().ToString(),
            Url = $"https://example.com/e2e-no-cover-{Guid.NewGuid()}.xml",
            Title = "E2E No Cover Show",
            Category = "Comedy",
            CreatedAt = DateTime.UtcNow
        };

        var client = new MongoClient("mongodb://localhost:27019");
        var feedsCollection = client.GetDatabase("OhrbobData").GetCollection<Feed>("FeedCollection");
        await feedsCollection.InsertOneAsync(feed);

        try
        {
            await Page.GotoAsync($"{_factory.ServerAddress}/Discover");

            var card = Page.Locator(".discover-lane .card-feed", new PageLocatorOptions { HasTextString = "E2E No Cover Show" }).First;
            await Expect(card).ToBeVisibleAsync();
            await Expect(card.Locator(".card-feed-cover-empty")).ToBeVisibleAsync();
            await Expect(card.Locator("img.card-feed-cover")).ToHaveCountAsync(0);
        }
        finally
        {
            await feedsCollection.DeleteOneAsync(f => f.Id == feed.Id);
        }
    }

    [Test]
    public async Task Discover_Card_TogglesDescriptionOverlay()
    {
        var feed = new Feed
        {
            Id = Guid.NewGuid().ToString(),
            Url = $"https://example.com/e2e-details-{Guid.NewGuid()}.xml",
            Title = "E2E Details Show",
            Category = "Comedy",
            Description = "<p>An <b>insightful</b> show.</p>",
            CreatedAt = DateTime.UtcNow
        };

        var client = new MongoClient("mongodb://localhost:27019");
        var feedsCollection = client.GetDatabase("OhrbobData").GetCollection<Feed>("FeedCollection");
        await feedsCollection.InsertOneAsync(feed);

        try
        {
            await Page.GotoAsync($"{_factory.ServerAddress}/Discover");

            var card = Page.Locator(".discover-lane .podcast-card", new PageLocatorOptions { HasTextString = "E2E Details Show" }).First;
            var overlay = card.Locator(".podcast-details-overlay");
            var toggle = card.Locator(".podcast-details-toggle");

            await Expect(card).ToBeVisibleAsync();
            await Expect(overlay).ToHaveTextAsync("An insightful show.");
            await Expect(overlay).ToBeHiddenAsync();

            await card.HoverAsync();
            await Expect(overlay).ToBeVisibleAsync();
            await Page.Mouse.MoveAsync(0, 0);
            await Expect(overlay).ToBeHiddenAsync();

            await toggle.ClickAsync();
            await Expect(card).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bis-open\\b"));
            await Expect(toggle).ToHaveAttributeAsync("aria-expanded", "true");
            await Expect(overlay).ToBeVisibleAsync();

            await toggle.ClickAsync();
            await Expect(card).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bis-open\\b"));
            await Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");
        }
        finally
        {
            await feedsCollection.DeleteOneAsync(f => f.Id == feed.Id);
        }
    }

    [Test]
    public async Task Discover_QueryParameter_PrefillsSearch_AndCategoryButtonsSearch()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Discover?q=maus");

        var searchInput = Page.Locator("#discover-search");
        await Expect(searchInput).ToHaveValueAsync("maus");
        await Expect(Page.Locator("#recommendation-lanes")).Not.ToBeVisibleAsync();
        await Expect(Page.Locator("#search-results .col").First).ToBeVisibleAsync(new() { Timeout = 15000 });

        var categoryButton = Page.Locator("#category-roster a").First;
        await Expect(categoryButton).ToBeVisibleAsync();
        await categoryButton.ClickAsync();
        await Page.WaitForURLAsync("**/Discover?q=*");
    }

    [Test]
    public async Task Discover_AddFeed_WhenLimitExceeded_ShowsToast_AndFormStaysVisible()
    {
        Page.Console += (_, msg) => Console.WriteLine($"BROWSER CONSOLE: {msg.Text}");
        Page.PageError += (_, err) => Console.WriteLine($"BROWSER PAGE ERROR: {err}");

        await Page.GotoAsync(_factory.ServerAddress);

        // Create 2 stations first (which is the Free tier limit)
        for (int i = 1; i <= 2; i++)
        {
            await Page.ClickAsync("#profileDropdown");
            await Page.ClickAsync("text=\"Stations\"");
            await Page.ClickAsync("text=Create New Station");
            await Page.FillAsync("input[name='Name']", $"Station {i}");
            await Page.FillAsync("input[name='Feeds']", "https://example.com/compiling-test-feed.xml");
            var submitBtn = Page.Locator("input[type='submit']");
            await submitBtn.ClickAsync();
            await Page.WaitForURLAsync("**/Stations");
        }

        // Go to Discover page
        await Page.GotoAsync($"{_factory.ServerAddress}/Discover");

        // Search for a podcast
        await Page.FillAsync("input[name='query']", "maus");
        await Page.PressAsync("input[name='query']", "Enter");

        // Wait for results
        var searchCard = Page.Locator("#search-results .col").First;
        await searchCard.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

        // Click the circular '+' overlay button to open the modal
        await searchCard.Locator("button:has(.bi-plus-lg)").ClickAsync();

        // Retrieve modal id and target the modal container
        var modalId = await searchCard.Locator("button:has(.bi-plus-lg)").GetAttributeAsync("data-bs-target");
        var modal = Page.Locator(modalId);
        await Expect(modal).ToBeVisibleAsync();

        // Ensure "Create New Station" option is checked (stations exist, so it is unchecked by default)
        var createNewCheck = modal.Locator("input[name='createNewStation']");
        await createNewCheck.CheckAsync();

        // Confirm Add (this should fail because of limit exceeded!)
        var confirmBtn = modal.Locator("button:has-text('Confirm Add')");
        await confirmBtn.ClickAsync();

        // Verify that the red Toast notification appears with the limit exceeded message!
        var toastBody = Page.Locator("#toast-body-text");
        await Expect(toastBody).ToHaveTextAsync("Station limit reached for Free tier.", new() { Timeout = 10000 });

        // Verify that the Toast container has the red background class
        var toast = Page.Locator("#liveToast");
        await Expect(toast).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("bg-danger"));

        // Crucial: Verify that the form modal remains fully visible and intact!
        await Expect(modal).ToBeVisibleAsync();
        await Expect(confirmBtn).ToBeVisibleAsync();

        // Uncheck the "Create New" checkbox
        await createNewCheck.UncheckAsync();

        // Check the "Station 1" checkbox
        var station1Check = modal.Locator("input[name='stationIds']").First;
        await station1Check.CheckAsync();

        // Confirm Add again (this should now succeed!)
        await confirmBtn.ClickAsync();

        // Verify that the green success Toast notification appears!
        await Expect(toastBody).ToHaveTextAsync("Added podcast to: Station 1", new() { Timeout = 10000 });
        await Expect(toast).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("bg-success"));

        // Verify that the modal is now closed
        await Expect(modal).Not.ToBeVisibleAsync();
    }

    [Test]
    public async Task Discover_AddFeed_WhenAlreadyExists_AsksConfirmation()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Discover");

        // Search for a podcast (we use compiles feed)
        await Page.FillAsync("input[name='query']", "maus");
        await Page.PressAsync("input[name='query']", "Enter");

        // Wait for results
        var searchCard = Page.Locator("#search-results .col").First;
        await searchCard.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

        // Extract feed URL from the first card's hidden input
        var feedUrlInput = searchCard.Locator("input[name='feedUrl']");
        var feedUrl = await feedUrlInput.GetAttributeAsync("value");
        Assert.That(feedUrl, Is.Not.Null.And.Not.Empty);

        // Go to Stations and create 1 station "Duplicate Station" with that dynamic feed URL
        await Page.ClickAsync("#profileDropdown");
        await Page.ClickAsync("text=\"Stations\"");
        await Page.ClickAsync("text=Create New Station");
        await Page.FillAsync("input[name='Name']", "Duplicate Station");
        await Page.FillAsync("input[name='Feeds']", feedUrl);
        var submitBtn = Page.Locator("input[type='submit']");
        await submitBtn.ClickAsync();
        await Page.WaitForURLAsync("**/Stations");

        // Go back to Discover page
        await Page.GotoAsync($"{_factory.ServerAddress}/Discover");

        // Search for a podcast again
        await Page.FillAsync("input[name='query']", "maus");
        await Page.PressAsync("input[name='query']", "Enter");

        // Wait for results
        searchCard = Page.Locator("#search-results .col").First;
        await searchCard.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

        // Click the circular '+' overlay button to open the modal
        await searchCard.Locator("button:has(.bi-plus-lg)").ClickAsync();

        var modalId = await searchCard.Locator("button:has(.bi-plus-lg)").GetAttributeAsync("data-bs-target");
        var modal = Page.Locator(modalId);
        await Expect(modal).ToBeVisibleAsync();

        // Uncheck "Create New Station" if it is checked
        var createNewCheck = modal.Locator("input[name='createNewStation']");
        if (await createNewCheck.IsCheckedAsync())
        {
            await createNewCheck.UncheckAsync();
        }

        // Check the checkbox for "Duplicate Station"
        var duplicateCheck = modal.Locator("input[name='stationIds']");
        await duplicateCheck.CheckAsync();

        // Confirm Add (this should trigger the Bootstrap confirmation modal!)
        var confirmBtn = modal.Locator("button:has-text('Confirm Add')");
        await confirmBtn.ClickAsync();

        // Locate the Bootstrap modal elements
        var confirmationModal = Page.Locator("#confirmationModal");
        var modalBody = Page.Locator("#confirmationModalBody");
        var cancelBtn = Page.Locator("#confirmationModalCancelBtn");
        var okBtn = Page.Locator("#confirmationModalConfirmBtn");

        // Verify that the Bootstrap modal is shown and contains the duplicate warning message
        await Expect(confirmationModal).ToBeVisibleAsync();
        await Expect(modalBody).ToHaveTextAsync("The station 'Duplicate Station' already has this podcast. Are you sure you want to add it again?");

        // Click Cancel on the modal (this should dismiss the modal and NOT submit the form)
        await cancelBtn.ClickAsync();

        // Verify that the modal is now hidden/dismissed
        await Expect(confirmationModal).Not.ToBeVisibleAsync();

        // Verify that the original modal is still visible
        await Expect(modal).ToBeVisibleAsync();
        await Expect(confirmBtn).ToBeVisibleAsync();

        // Confirm Add again to trigger the modal again
        await confirmBtn.ClickAsync();

        // Verify modal is visible again
        await Expect(confirmationModal).ToBeVisibleAsync();

        // Click OK/Confirm on the modal (this should submit the form and complete successfully!)
        await okBtn.ClickAsync();

        // Verify that the modal is now hidden/dismissed
        await Expect(confirmationModal).Not.ToBeVisibleAsync();

        // Verify that the green success Toast notification appears!
        var toastBody = Page.Locator("#toast-body-text");
        await Expect(toastBody).ToHaveTextAsync("Added podcast to: Duplicate Station", new() { Timeout = 10000 });

        // Verify in the database that the feed was actually added TWICE to the station
        var client = new MongoClient("mongodb://localhost:27019");
        var database = client.GetDatabase("OhrbobData");
        var stationsCollection = database.GetCollection<Station>("StationCollection");
        var updatedStation = await stationsCollection.Find(s => s.AccountId == _testAccountId && s.Name == "Duplicate Station").FirstOrDefaultAsync();
        Assert.That(updatedStation, Is.Not.Null);
        Assert.That(updatedStation.Feeds.Count(f => f == feedUrl), Is.EqualTo(2));
        Assert.That(updatedStation.Schedule.Feeds.Count(f => f.Url == feedUrl), Is.EqualTo(2));
    }
}