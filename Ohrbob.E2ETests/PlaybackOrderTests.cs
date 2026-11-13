using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using Ohrbob.E2ETests.Infrastructure;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Configuration;
using Ohrbob.Web.Services;

namespace Ohrbob.E2ETests;

/// <summary>
/// End-to-end coverage for station playback order, exercised while something is actively
/// playing in ohrbob.web. The station's schedule (per-feed sort order and the set of feeds)
/// is changed in the Edit view; playback must keep running and the "up next" episode shown
/// by the audio widget must track the server's <c>/stream/status</c>.
///
/// Feeds come from the deterministic <c>podcast-testcasts</c> host (which ships with the
/// stack via <c>docker compose up -d testcasts</c>, see testdata/ohrbob-testcasts). The
/// testcasts server dates the first configured episode as the newest and steps one day back
/// per following episode, so a feed configured as [C, B, A] plays oldest-to-newest as A, B, C.
///
/// Address selection: the test process loads fixtures from <c>OHRBOB_TESTCASTS_URL</c>
/// (default <c>http://localhost:5004</c>), while the station feed URLs use
/// <c>OHRBOB_TESTCASTS_FEED_URL</c> (default: the same). When the Api runs inside Docker
/// Compose, set <c>OHRBOB_TESTCASTS_FEED_URL=http://testcasts:5004</c>.
/// </summary>
[NonParallelizable]
public class PlaybackOrderTests : PageTest
{
    private PlaywrightWebApplicationFactory _factory = null!;
    private string _accountId = string.Empty;
    private string _profileId = string.Empty;

    public override BrowserNewContextOptions ContextOptions() => new() { IgnoreHTTPSErrors = true };

    [SetUp]
    public async Task Setup()
    {
        await Context.ClearCookiesAsync();

        if (!await TestCasts.IsAvailableAsync())
        {
            Assert.Ignore(
                $"podcast-testcasts is not reachable at {TestCasts.ConfigureBaseUrl}. Start it via " +
                "\"docker compose up -d testcasts\" (see docker-compose.yml and testdata/ohrbob-testcasts), " +
                "or point OHRBOB_TESTCASTS_URL at a running instance. When the Api runs in Docker, also set " +
                "OHRBOB_TESTCASTS_FEED_URL=http://testcasts:5004 so stored feed URLs use the service name.");
        }

        _factory = new PlaywrightWebApplicationFactory();
        Page.Console += (_, msg) => Console.WriteLine($"BROWSER CONSOLE: {msg.Text}");
        Page.PageError += (_, msg) => Console.WriteLine($"BROWSER ERROR: {msg}");
        await InstallMockAudioAsync();

        var accountService = _factory.Services.GetRequiredService<AccountApiService>();
        var account = await accountService.CreateOrUpdateAccountAsync(
            "Playback Order", $"playback-order-{Guid.NewGuid()}@test.com", "", "Test", Guid.NewGuid().ToString());
        await accountService.CompleteOnboardingAsync(account!.Id);
        _accountId = account.Id;

        var profileService = _factory.Services.GetRequiredService<ProfilesApiService>();
        var profiles = await profileService.GetProfilesByAccountIdAsync(_accountId);
        if (profiles == null || profiles.Count == 0)
        {
            await profileService.CreateProfileAsync(_accountId, new Profile { Name = "Main Profile" });
            profiles = await profileService.GetProfilesByAccountIdAsync(_accountId);
        }
        _profileId = profiles!.First().Id;

        TestAuthHandler.TestAccountId = _accountId;
        _factory.CreateClient();
    }

    [TearDown]
    public async Task TearDown()
    {
        await Context.CloseAsync();
        // SetUp may have called Assert.Ignore before the factory was created.
        _factory?.Dispose();
        TestAuthHandler.TestAccountId = string.Empty;
    }

    [Test]
    public async Task ChangingFeedPlaybackOrder_UpdatesUpcomingWithoutInterruptingPlayback()
    {
        // One feed, three episodes: configured [C, B, A] -> C is newest, A is oldest.
        var slug = Slug("order");
        await TestCasts.ConfigureAsync(new TestCastPodcast(slug, "Order Feed", new[]
        {
            new TestCastEpisode("Episode C", $"{slug}-c.mp3"),
            new TestCastEpisode("Episode B", $"{slug}-b.mp3"),
            new TestCastEpisode("Episode A", $"{slug}-a.mp3"),
        }));

        var station = await CreateStationAsync("Order Station", new[] { TestCasts.FeedUrl(slug) }, minimumEpisodes: 3);
        await PlayStationAndWaitAsync("Order Station");

        // Oldest-to-newest is the default: A (oldest) plays first, B is up next.
        await ExpectWidgetTrackAsync("Episode A");
        await ExpectWidgetNextAsync("Episode B");
        await PersistCurrentPlayheadAsync(station.Id);
        var srcBeforeChange = await LoadedStreamUriAsync();

        // Flip the feed to "Backwards" (newest to oldest) on the Edit view.
        await OpenStationEditViaGearAsync("Order Station");
        await SetFeedSortOrderAsync(0, FeedSortOrder.NewestToOldest);

        // Backwards yields [C, B, A]; after the currently playing A the first unplayed is C.
        await WaitForServerNextAsync("Episode C");

        // The widget's "up next" must reflect the new order too...
        await ExpectWidgetNextAsync("Episode C", timeoutMs: 20000);

        // ...without interrupting the currently playing episode.
        Assert.That(await LoadedStreamUriAsync(), Is.EqualTo(srcBeforeChange),
            "Changing the playback order must not reload/interrupt the current episode");
        Assert.That(await IsWidgetPlayingAsync(), Is.True,
            "Changing the playback order must not stop playback");
    }

    [Test]
    public async Task RemovingAFeed_UpdatesUpcomingWithoutInterruptingPlayback()
    {
        var slug1 = Slug("remove-main");
        var slug2 = Slug("remove-other");
        await TestCasts.ConfigureAsync(
            new TestCastPodcast(slug1, "Main Feed", new[]
            {
                new TestCastEpisode("Episode C", $"{slug1}-c.mp3"),
                new TestCastEpisode("Episode B", $"{slug1}-b.mp3"),
                new TestCastEpisode("Episode A", $"{slug1}-a.mp3"),
            }),
            new TestCastPodcast(slug2, "Other Feed", new[]
            {
                new TestCastEpisode("Episode D", $"{slug2}-d.mp3"),
            }));

        // Rotation of one episode per feed: A (main), D (other), B, C.
        var station = await CreateStationAsync(
            "Remove Feed Station",
            new[] { TestCasts.FeedUrl(slug1), TestCasts.FeedUrl(slug2) },
            minimumEpisodes: 4);
        await PlayStationAndWaitAsync("Remove Feed Station");

        await ExpectWidgetTrackAsync("Episode A");
        await ExpectWidgetNextAsync("Episode D");
        await PersistCurrentPlayheadAsync(station.Id);
        var srcBeforeChange = await LoadedStreamUriAsync();

        // Remove the other feed while A is playing.
        await OpenStationEditViaGearAsync("Remove Feed Station");
        await RemoveFeedAsync(TestCasts.FeedUrl(slug2));

        // Remaining feed order is A, B, C -> next is B.
        await WaitForServerNextAsync("Episode B");
        await ExpectWidgetNextAsync("Episode B", timeoutMs: 20000);

        Assert.That(await LoadedStreamUriAsync(), Is.EqualTo(srcBeforeChange),
            "Removing a feed must not reload/interrupt the current episode");
        Assert.That(await IsWidgetPlayingAsync(), Is.True,
            "Removing a feed must not stop playback");
    }

    [Test]
    public async Task AddingAFeed_UpdatesUpcomingWithoutInterruptingPlayback()
    {
        var slug1 = Slug("add-main");
        var slug2 = Slug("add-new");
        // Both feeds exist on the testcasts server, but the station starts with only the first.
        await TestCasts.ConfigureAsync(
            new TestCastPodcast(slug1, "Existing Feed", new[]
            {
                new TestCastEpisode("Episode C", $"{slug1}-c.mp3"),
                new TestCastEpisode("Episode B", $"{slug1}-b.mp3"),
                new TestCastEpisode("Episode A", $"{slug1}-a.mp3"),
            }),
            new TestCastPodcast(slug2, "Added Feed", new[]
            {
                new TestCastEpisode("Episode Y", $"{slug2}-y.mp3"),
                new TestCastEpisode("Episode X", $"{slug2}-x.mp3"),
            }));

        var station = await CreateStationAsync("Add Feed Station", new[] { TestCasts.FeedUrl(slug1) }, minimumEpisodes: 3);
        await PlayStationAndWaitAsync("Add Feed Station");

        await ExpectWidgetTrackAsync("Episode A");
        await ExpectWidgetNextAsync("Episode B");
        await PersistCurrentPlayheadAsync(station.Id);
        var srcBeforeChange = await LoadedStreamUriAsync();

        // Add the second feed while A is playing.
        await OpenStationEditViaGearAsync("Add Feed Station");
        await AddFeedAsync(TestCasts.FeedUrl(slug2));

        // Wait until the newly added feed's episodes have been parsed server-side.
        await WaitForEpisodesAsync(station.Id, 5);

        // Rotation becomes A (existing), X (added), B, Y, C -> next is X.
        await WaitForServerNextAsync("Episode X");
        await ExpectWidgetNextAsync("Episode X", timeoutMs: 30000);

        Assert.That(await LoadedStreamUriAsync(), Is.EqualTo(srcBeforeChange),
            "Adding a feed must not reload/interrupt the current episode");
        Assert.That(await IsWidgetPlayingAsync(), Is.True,
            "Adding a feed must not stop playback");
    }

    [Test]
    public async Task RemovingThePlayingFeed_MovesPlaybackToAnotherFeed()
    {
        var slug1 = Slug("playing-removed");
        var slug2 = Slug("playing-survivor");
        await TestCasts.ConfigureAsync(
            new TestCastPodcast(slug1, "Doomed Feed", new[]
            {
                new TestCastEpisode("Episode C", $"{slug1}-c.mp3"),
                new TestCastEpisode("Episode B", $"{slug1}-b.mp3"),
                new TestCastEpisode("Episode A", $"{slug1}-a.mp3"),
            }),
            new TestCastPodcast(slug2, "Survivor Feed", new[]
            {
                new TestCastEpisode("Episode D", $"{slug2}-d.mp3"),
            }));

        var station = await CreateStationAsync(
            "Playing Feed Station",
            new[] { TestCasts.FeedUrl(slug1), TestCasts.FeedUrl(slug2) },
            minimumEpisodes: 4);
        await PlayStationAndWaitAsync("Playing Feed Station");

        await ExpectWidgetTrackAsync("Episode A");
        await PersistCurrentPlayheadAsync(station.Id);

        // Remove the feed that is currently playing.
        await OpenStationEditViaGearAsync("Playing Feed Station");
        await RemoveFeedAsync(TestCasts.FeedUrl(slug1));

        // The removed episode is no longer part of the station, so playback must move to the
        // only remaining feed's episode, D, and keep playing there.
        await ExpectWidgetTrackAsync("Episode D", timeoutMs: 20000);
        await ExpectLoadedStreamUriContainsAsync($"{slug2}-d.mp3", timeoutMs: 20000);
        Assert.That(await IsWidgetPlayingAsync(), Is.True,
            "Removing the playing feed must keep playback going with the next available episode");
    }

    [Test]
    public async Task ChangingFeedSpan_UpdatesUpcomingWithoutInterruptingPlayback()
    {
        var slug1 = Slug("span-main");
        var slug2 = Slug("span-other");
        await TestCasts.ConfigureAsync(
            new TestCastPodcast(slug1, "Span Feed", new[]
            {
                new TestCastEpisode("Episode C", $"{slug1}-c.mp3"),
                new TestCastEpisode("Episode B", $"{slug1}-b.mp3"),
                new TestCastEpisode("Episode A", $"{slug1}-a.mp3"),
            }),
            new TestCastPodcast(slug2, "Other Feed", new[]
            {
                new TestCastEpisode("Episode E", $"{slug2}-e.mp3"),
                new TestCastEpisode("Episode D", $"{slug2}-d.mp3"),
            }));

        // Span 1 per feed: A (main), D (other), B, E, C.
        var station = await CreateStationAsync(
            "Span Station",
            new[] { TestCasts.FeedUrl(slug1), TestCasts.FeedUrl(slug2) },
            minimumEpisodes: 5);
        await PlayStationAndWaitAsync("Span Station");

        await ExpectWidgetTrackAsync("Episode A");
        await ExpectWidgetNextAsync("Episode D");
        await PersistCurrentPlayheadAsync(station.Id);
        var srcBeforeChange = await LoadedStreamUriAsync();

        // Widen the main feed's span to 3 while A is playing.
        await OpenStationEditViaGearAsync("Span Station");
        await SetFeedSpanAsync(0, 3);

        // With span 3 the rotation becomes A, B, C, D, E -> next is B.
        await WaitForServerNextAsync("Episode B");
        await ExpectWidgetNextAsync("Episode B", timeoutMs: 20000);

        Assert.That(await LoadedStreamUriAsync(), Is.EqualTo(srcBeforeChange),
            "Changing a feed's span must not reload/interrupt the current episode");
        Assert.That(await IsWidgetPlayingAsync(), Is.True,
            "Changing a feed's span must not stop playback");
    }

    [Test]
    public async Task RemotePlayhead_IsReflectedWhileThisWidgetIsPaused()
    {
        var slug = Slug("remote-sync");
        await TestCasts.ConfigureAsync(new TestCastPodcast(slug, "Remote Feed", new[]
        {
            new TestCastEpisode("Episode B", $"{slug}-b.mp3"),
            new TestCastEpisode("Episode A", $"{slug}-a.mp3"),
        }));

        var station = await CreateStationAsync("Remote Sync Station", new[] { TestCasts.FeedUrl(slug) }, minimumEpisodes: 2);

        // Activate the station server-side without starting playback in this browser.
        await PostPlayerAsync($"/stream/play-station/{station.Id}");

        // Never auto-play here: this browser stands in for a paused desktop view while the
        // mobile app drives the session through the Player API.
        await Context.AddCookiesAsync(new[]
        {
            new Cookie { Name = "ohrbob_user_paused", Value = "1", Url = _factory.ServerAddress }
        });

        await Page.GotoAsync(_factory.ServerAddress);
        await ExpectWidgetTrackAsync("Episode A");
        Assert.That(await IsWidgetPlayingAsync(), Is.False);

        var status = await GetPlayerStatusAsync();
        var currentId = status!.Value.GetProperty("current").GetProperty("episodeId").GetString();
        var nextId = status.Value.GetProperty("next").GetProperty("episodeId").GetString();

        // Mobile reports progress on the current episode -> the paused widget follows.
        await PostPlayerStatusAsync(currentId!, station.Id, 120);
        await Page.WaitForFunctionAsync(
            "() => document.querySelector('#ohrbob-player').currentTime === 120",
            null, new() { Timeout = 20000 });

        // Mobile moves on -> the widget shows the new episode, still without playing.
        await PostPlayerStatusAsync(nextId!, station.Id, 0);
        await ExpectWidgetTrackAsync("Episode B", timeoutMs: 20000);
        Assert.That(await IsWidgetPlayingAsync(), Is.False);
    }

    [Test]
    public async Task Widget_StripsHtmlFromEpisodeTitle()
    {
        var slug = Slug("html-title");
        await TestCasts.ConfigureAsync(new TestCastPodcast(slug, "HTML Feed", new[]
        {
            new TestCastEpisode("Episode <b>Bold</b>", $"{slug}-1.mp3")
        }));

        await CreateStationAsync("HTML Title Station", new[] { TestCasts.FeedUrl(slug) }, minimumEpisodes: 1);
        await PlayStationAndWaitAsync("HTML Title Station");

        await ExpectWidgetTrackAsync("Episode Bold");
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    private static string Slug(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private async Task<Station> CreateStationAsync(string name, string[] feedUrls, int minimumEpisodes)
    {
        var stations = _factory.Services.GetRequiredService<StationsApiService>();
        var station = await stations.CreateStationAsync(_accountId, new Station
        {
            AccountId = _accountId,
            Name = name,
            Feeds = feedUrls
        });

        // The dashboard only lists stations assigned to the active profile.
        var profileService = _factory.Services.GetRequiredService<ProfilesApiService>();
        await profileService.AddStationToProfileAsync(_profileId, station!.Id);

        // Wait until the API's background worker has parsed the feeds' episodes.
        await WaitForEpisodesAsync(station.Id, minimumEpisodes);
        return station;
    }

    private async Task WaitForEpisodesAsync(string stationId, int minimum)
    {
        var stations = _factory.Services.GetRequiredService<StationsApiService>();
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var details = await stations.GetStationDetailsByIdAsync(stationId);
            var count = details?.Feeds?.Sum(f => f.EpisodeCount) ?? 0;
            if (count >= minimum) return;
            await Task.Delay(500);
        }
        Assert.Fail($"Feed episodes were not parsed within the timeout (station {stationId}).");
    }

    private ILocator StationCard(string name) =>
        Page.Locator(".card-station", new PageLocatorOptions { HasTextString = name }).First;

    private async Task PlayStationAndWaitAsync(string stationName)
    {
        await Page.GotoAsync(_factory.ServerAddress);
        var card = StationCard(stationName);
        await card.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20000 });

        // Start playback from the station card (explicit user action).
        await card.Locator(".card-station-play").ClickAsync();

        var playBtn = Page.Locator("#ohrbob-player .js-play");
        if (await playBtn.Locator("rect").CountAsync() == 0)
        {
            await Expect(Page.Locator("#ohrbob-player")).Not.ToHaveAttributeAsync("track", "Track Name",
                new() { Timeout = 30000 });
            await playBtn.ClickAsync();
        }
        await Expect(playBtn.Locator("rect")).ToHaveCountAsync(2, new() { Timeout = 30000 });
    }

    private async Task OpenStationEditViaGearAsync(string stationName)
    {
        // Clicking the gear performs an htmx-boosted swap of #main-content, so the
        // audio widget (outside #main-content) keeps playing.
        await StationCard(stationName).Locator("a[title='Manage Station']").ClickAsync();
        await Expect(Page.Locator("h1")).ToContainTextAsync("Edit Station", new() { Timeout = 15000 });
    }

    private async Task SetFeedSortOrderAsync(int feedIndex, FeedSortOrder order)
    {
        var title = order switch
        {
            FeedSortOrder.NewestToOldest => "Backwards: Newest to Oldest",
            FeedSortOrder.Randomized => "Random",
            _ => "Forwards: Oldest to Newest"
        };

        var row = Page.Locator("#rotationContainer .rotation-item").Nth(feedIndex);
        await Expect(row).ToBeVisibleAsync(new() { Timeout = 15000 });
        var button = row.Locator($"button.playback-node-btn[title='{title}']");

        // Trigger the control and wait for the schedule to be persisted. Some environments
        // swallow synthetic clicks on the slider nodes (Bootstrap tooltips), so fall back to
        // invoking the same handler the inline onclick calls.
        try
        {
            await Page.RunAndWaitForResponseAsync(
                async () => await button.DispatchEventAsync("click"),
                r => r.Url.Contains("/Stations/UpdateSchedule/"),
                new PageRunAndWaitForResponseOptions { Timeout = 8000 });
        }
        catch (TimeoutException)
        {
            await Page.RunAndWaitForResponseAsync(
                async () => await Page.EvaluateAsync(
                    "([hashId, value]) => window.setPlaybackOrder(hashId, value)",
                    new object[] { feedIndex.ToString(), (int)order }),
                r => r.Url.Contains("/Stations/UpdateSchedule/"));
        }
    }

    private async Task SetFeedSpanAsync(int feedIndex, int span)
    {
        var row = Page.Locator("#rotationContainer .rotation-item").Nth(feedIndex);
        await Expect(row).ToBeVisibleAsync(new() { Timeout = 15000 });
        var spanInput = row.Locator(".episodes-input");
        await Expect(spanInput).ToBeVisibleAsync(new() { Timeout = 15000 });
        await spanInput.FillAsync(span.ToString());

        await Page.RunAndWaitForResponseAsync(
            async () => await spanInput.DispatchEventAsync("change"),
            r => r.Url.Contains("/Stations/UpdateSchedule/"));
    }

    private async Task AddFeedAsync(string feedUrl)
    {
        await Page.ClickAsync("#stationMoreMenuBtn");
        await Expect(Page.Locator("#manualAddBtn")).ToBeVisibleAsync();
        await Page.ClickAsync("#manualAddBtn");
        await Expect(Page.Locator("#manualAddFeedModal")).ToBeVisibleAsync();
        await Page.FillAsync("#newFeedUrl", feedUrl);

        await Page.RunAndWaitForResponseAsync(
            async () => await Page.ClickAsync("#submitManualAddBtn"),
            r => r.Url.Contains("/Stations/UpdateSchedule/"));
    }

    private async Task RemoveFeedAsync(string feedUrl)
    {
        var row = Page.Locator($"#rotationContainer .rotation-item[data-url=\"{feedUrl}\"]");
        await Expect(row).ToHaveCountAsync(1, new() { Timeout = 15000 });
        await row.Locator(".feed-actions-menu").ClickAsync();
        await Expect(row.Locator(".remove-feed")).ToBeVisibleAsync();
        await row.Locator(".remove-feed").ClickAsync();

        await Expect(Page.Locator("#confirmRemoveFeedModal")).ToBeVisibleAsync(new() { Timeout = 10000 });
        await Page.RunAndWaitForResponseAsync(
            async () => await Page.ClickAsync("#confirmRemoveFeedBtn"),
            r => r.Url.Contains("/Stations/UpdateSchedule/"));
    }

    private async Task ExpectWidgetTrackAsync(string title, int timeoutMs = 30000)
    {
        await Expect(Page.Locator("#ohrbob-player")).ToHaveAttributeAsync(
            "track", title, new() { Timeout = timeoutMs });
    }

    private async Task ExpectWidgetNextAsync(string title, int timeoutMs = 30000)
    {
        await Expect(Page.Locator("#ohrbob-player")).ToHaveAttributeAsync(
            "next-episode", new Regex($"\"track\":\"{Regex.Escape(title)}\""),
            new() { Timeout = timeoutMs });
    }

    private async Task ExpectLoadedStreamUriContainsAsync(string fragment, int timeoutMs = 20000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        var observed = await LoadedStreamUriAsync();
        while (DateTime.UtcNow < deadline)
        {
            observed = await LoadedStreamUriAsync();
            if (observed != null && observed.Contains(fragment)) return;
            await Task.Delay(200);
        }
        Assert.Fail($"Loaded stream URI never contained '{fragment}' (last observed: '{observed ?? "<none>"}').");
    }

    private async Task<bool> IsWidgetPlayingAsync() =>
        await Page.EvaluateAsync<bool>("() => document.querySelector('#ohrbob-player').isPlaying");

    private async Task<string?> LoadedStreamUriAsync() =>
        await Page.EvaluateAsync<string?>("() => window._ohrbobAudio ? window._ohrbobAudio.src : null");

    private async Task PersistCurrentPlayheadAsync(string stationId)
    {
        // The widget persists its playhead (and last-active episode) through
        // POST /stream/status; do the same so the server's playhead matches the UI before we
        // change the schedule (otherwise the server would re-derive the current episode).
        var status = await GetPlayerStatusAsync();
        var episodeId = status is { } s && s.TryGetProperty("current", out var current) && current.ValueKind == JsonValueKind.Object
            ? current.GetProperty("episodeId").GetString()
            : null;
        Assert.That(episodeId, Is.Not.Null.And.Not.Empty, "Could not resolve the currently playing episode id.");

        await PostPlayerStatusAsync(episodeId!, stationId, 0);
    }

    private async Task PostPlayerStatusAsync(string episodeId, string stationId, int positionSeconds)
    {
        var profile = await GetProfileAsync();
        using var client = new HttpClient { BaseAddress = new Uri(PlayerApiUrl()) };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/stream/status")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { episodeId, stationId, positionSeconds }),
                Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Stream-Token", profile.StreamToken);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private async Task PostPlayerAsync(string path)
    {
        var profile = await GetProfileAsync();
        using var client = new HttpClient { BaseAddress = new Uri(PlayerApiUrl()) };
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("X-Stream-Token", profile.StreamToken);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private async Task<Profile> GetProfileAsync()
    {
        var profileService = _factory.Services.GetRequiredService<ProfilesApiService>();
        var profiles = await profileService.GetProfilesByAccountIdAsync(_accountId);
        return profiles!.First();
    }

    private async Task<JsonElement?> GetPlayerStatusAsync()
    {
        var profile = await GetProfileAsync();
        using var client = new HttpClient { BaseAddress = new Uri(PlayerApiUrl()) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/stream/status");
        request.Headers.Add("X-Stream-Token", profile.StreamToken);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(text) ? null : JsonSerializer.Deserialize<JsonElement>(text);
    }

    private async Task WaitForServerNextAsync(string expectedTitle, int timeoutMs = 20000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        string? observed = null;
        while (DateTime.UtcNow < deadline)
        {
            var status = await GetPlayerStatusAsync();
            if (status is { } s && s.TryGetProperty("next", out var next) && next.ValueKind == JsonValueKind.Object)
            {
                observed = next.TryGetProperty("title", out var t) ? t.GetString() : null;
                if (observed == expectedTitle) return;
            }
            await Task.Delay(250);
        }
        Assert.Fail($"Server's next episode never became '{expectedTitle}' (last observed: '{observed ?? "<none>"}').");
    }

    private string PlayerApiUrl() =>
        _factory.Services.GetRequiredService<IOptions<OhrbobApiOptions>>().Value.PlayerApiUrl;

    private async Task InstallMockAudioAsync()
    {
        // Replace HTMLMediaElement playback with an instant, deterministic mock so the tests
        // do not depend on real media, buffering, or network latency.
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
                        const type = event.type;
                        if (this._listeners[type]) this._listeners[type].forEach(cb => cb(event));
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
                        this._paused = false;
                        setTimeout(() => {
                            this.dispatchEvent(new Event('play'));
                            this.dispatchEvent(new Event('playing'));
                        }, 10);
                        return Promise.resolve();
                    }
                    pause() {
                        this._paused = true;
                        setTimeout(() => this.dispatchEvent(new Event('pause')), 10);
                    }
                    load() {}
                }
                window.Audio = MockAudio;
                if (!('mediaSession' in navigator)) {
                    navigator.mediaSession = {
                        metadata: null,
                        playbackState: 'none',
                        setActionHandler: function (action, callback) { if (!this._handlers) this._handlers = {}; this._handlers[action] = callback; },
                        setPositionState: function (state) { this._positionState = state; }
                    };
                }
            })();
        ");
    }
}
