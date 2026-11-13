using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using Ohrbob.E2ETests.Infrastructure;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Services;

namespace Ohrbob.E2ETests;

/// <summary>
/// Reproduction for issue #5: when an episode concludes, playback must advance to the next
/// episode in the playlist automatically (+ keep playing). These tests drive the same
/// <c>ended</c> handoff the real player uses, but with a deterministic mock media element so
/// they are fast and independent of real media.
/// </summary>
[NonParallelizable]
public class EndOfEpisodeAdvanceTests : PageTest
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
            Assert.Ignore($"podcast-testcasts is not reachable at {TestCasts.ConfigureBaseUrl}.");
        }

        _factory = new PlaywrightWebApplicationFactory();
        Page.Console += (_, msg) => Console.WriteLine($"BROWSER CONSOLE: {msg.Text}");
        Page.PageError += (_, msg) => Console.WriteLine($"BROWSER ERROR: {msg}");
        await InstallMockAudioAsync();

        var accountService = _factory.Services.GetRequiredService<AccountApiService>();
        var account = await accountService.CreateOrUpdateAccountAsync(
            "End Of Episode", $"ended-{Guid.NewGuid()}@test.com", "", "Test", Guid.NewGuid().ToString());
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
        _factory?.Dispose();
        TestAuthHandler.TestAccountId = string.Empty;
    }

    [Test]
    public async Task ConcludingEpisode_AdvancesToNextEpisodeAndKeepsPlaying()
    {
        var slug = $"{Guid.NewGuid():N}";
        await TestCasts.ConfigureAsync(new TestCastPodcast(slug, "Ended Feed", new[]
        {
            new TestCastEpisode("Episode C", $"{slug}-c.mp3"),
            new TestCastEpisode("Episode B", $"{slug}-b.mp3"),
            new TestCastEpisode("Episode A", $"{slug}-a.mp3"),
        }));

        await CreateStationAsync("Ended Station", new[] { TestCasts.FeedUrl(slug) }, minimumEpisodes: 3);
        await PlayStationAndWaitAsync("Ended Station");

        await ExpectWidgetTrackAsync("Episode A");
        await ExpectWidgetNextAsync("Episode B");
        Assert.That(await LoadedStreamUriAsync(), Does.Contain($"{slug}-a.mp3"));

        // Simulate the media element concluding the current episode.
        await FireEndedAsync();

        // The next episode must become current and actually be loaded/playing.
        await ExpectWidgetTrackAsync("Episode B", timeoutMs: 15000);
        await ExpectLoadedStreamUriContainsAsync($"{slug}-b.mp3", timeoutMs: 15000);
        Assert.That(await IsWidgetPlayingAsync(), Is.True,
            "Playback must continue after an episode ends");
        Assert.That(await IsAudioPausedAsync(), Is.False,
            "The audio element must actually be playing the next episode");
    }

    /// <summary>
    /// Documents the reported mobile/screen-locked failure: the browser accepts (resolves)
    /// <c>play()</c> for the newly-assigned next source but never actually starts it. The
    /// widget must recover once the page becomes visible again instead of staying stuck.
    /// </summary>
    [Test]
    public async Task ConcludingEpisode_WhenPlaybackIsSilentlyIgnoredWhileHidden_RecoversWhenVisible()
    {
        var slug = $"{Guid.NewGuid():N}";
        await TestCasts.ConfigureAsync(new TestCastPodcast(slug, "Hidden Feed", new[]
        {
            new TestCastEpisode("Episode C", $"{slug}-c.mp3"),
            new TestCastEpisode("Episode B", $"{slug}-b.mp3"),
            new TestCastEpisode("Episode A", $"{slug}-a.mp3"),
        }));

        await CreateStationAsync("Hidden Station", new[] { TestCasts.FeedUrl(slug) }, minimumEpisodes: 3);
        await PlayStationAndWaitAsync("Hidden Station");

        await ExpectWidgetTrackAsync("Episode A");
        await ExpectWidgetNextAsync("Episode B");

        // The browser is about to silently ignore the auto-start of the next source. Swallow
        // every play() attempt (including any recovery the periodic sync triggers) until we
        // explicitly let playback resume, so the "still paused" state below is deterministic.
        await Page.EvaluateAsync("() => { window.__mockIgnorePlayCount = 1000; }");
        await FireEndedAsync();

        // The optimistic UI advanced, but no audio is actually playing.
        await ExpectWidgetTrackAsync("Episode B", timeoutMs: 15000);
        Assert.That(await IsAudioPausedAsync(), Is.True,
            "Precondition: the mock browser ignored the background play() attempt");

        // The user unlocks the phone -> the browser may start playback again and the page
        // becomes visible/focused.
        await Page.EvaluateAsync("() => { window.__mockIgnorePlayCount = 0; }");

        // Playback must recover instead of staying stuck.
        await WaitForAudioPlayingAsync(20000);
        Assert.That(await IsWidgetPlayingAsync(), Is.True,
            "Playback must recover once the page is visible again");
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    private async Task<Station> CreateStationAsync(string name, string[] feedUrls, int minimumEpisodes)
    {
        var stations = _factory.Services.GetRequiredService<StationsApiService>();
        var station = await stations.CreateStationAsync(_accountId, new Station
        {
            AccountId = _accountId,
            Name = name,
            Feeds = feedUrls
        });

        var profileService = _factory.Services.GetRequiredService<ProfilesApiService>();
        await profileService.AddStationToProfileAsync(_profileId, station!.Id);

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

    private async Task PlayStationAndWaitAsync(string stationName)
    {
        await Page.GotoAsync(_factory.ServerAddress);
        var card = Page.Locator(".card-station", new PageLocatorOptions { HasTextString = stationName }).First;
        await card.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20000 });
        await card.Locator(".card-station-play").ClickAsync();

        var playBtn = Page.Locator("#ohrbob-player .js-play");
        await Expect(Page.Locator("#ohrbob-player")).Not.ToHaveAttributeAsync("track", "Track Name",
            new() { Timeout = 30000 });
        // The card's play action starts playback asynchronously; give it a moment before
        // falling back to the play button (clicking it would toggle a starting playback off).
        try
        {
            await Expect(playBtn.Locator("rect")).ToHaveCountAsync(2, new() { Timeout = 5000 });
        }
        catch (PlaywrightException)
        {
            await playBtn.ClickAsync();
        }
        await Expect(playBtn.Locator("rect")).ToHaveCountAsync(2, new() { Timeout = 30000 });
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

    private async Task FireEndedAsync() =>
        await Page.EvaluateAsync("() => window._ohrbobAudio.dispatchEvent(new Event('ended'))");

    private async Task DispatchVisibleAsync() =>
        await Page.EvaluateAsync(@"() => {
            document.dispatchEvent(new Event('visibilitychange'));
            window.dispatchEvent(new Event('focus'));
            window.dispatchEvent(new Event('pageshow'));
        }");

    private async Task WaitForAudioPlayingAsync(int timeoutMs = 15000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (!await IsAudioPausedAsync()) return;

            // Keep nudging the recovery while the page is "visible" (a transition may have
            // been in flight when the first visibility event fired).
            await DispatchVisibleAsync();
            await Task.Delay(250);
        }

        var state = await Page.EvaluateAsync<string>(@"() => JSON.stringify({
            paused: window._ohrbobAudio.paused,
            ended: window._ohrbobAudio.ended,
            autoPlayExpected: window._ohrbobAudio._ohrbobAutoPlayExpected,
            hidden: document.hidden
        })");
        Assert.Fail($"Audio never started after the page became visible again. State: {state}");
    }

    private async Task<bool> IsWidgetPlayingAsync() =>
        await Page.EvaluateAsync<bool>("() => document.querySelector('#ohrbob-player').isPlaying");

    private async Task<bool> IsAudioPausedAsync() =>
        await Page.EvaluateAsync<bool>("() => window._ohrbobAudio.paused");

    private async Task<string?> LoadedStreamUriAsync() =>
        await Page.EvaluateAsync<string?>("() => window._ohrbobAudio ? window._ohrbobAudio.src : null");

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

    private async Task InstallMockAudioAsync()
    {
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
                        this._ended = false;
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
                        // A real media element stops (and reports ended) when the media concludes.
                        if (type === 'ended') { this._paused = true; this._ended = true; }
                        if (type === 'play' || type === 'playing') { this._ended = false; }
                        if (this._listeners[type]) this._listeners[type].forEach(cb => cb(event));
                        if (this['on' + type]) this['on' + type](event);
                    }
                    get src() { return this._src; }
                    set src(val) { this._src = val; this._ended = false; }
                    get currentTime() { return this._currentTime; }
                    set currentTime(val) { this._currentTime = val; }
                    get duration() { return this._duration; }
                    set duration(val) { this._duration = val; }
                    get volume() { return this._volume; }
                    set volume(val) { this._volume = val; }
                    get muted() { return this._muted; }
                    set muted(val) { this._muted = val; }
                    get paused() { return this._paused; }
                    get ended() { return this._ended; }
                    play() {
                        if (window.__mockIgnorePlayCount > 0) {
                            // Simulate a browser that resolves play() but never actually starts a
                            // newly-assigned source while the page is hidden / screen locked.
                            window.__mockIgnorePlayCount--;
                            return Promise.resolve();
                        }
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
