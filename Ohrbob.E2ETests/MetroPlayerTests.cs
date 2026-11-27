using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;
using Ohrbob.E2ETests.Infrastructure;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Services;

namespace Ohrbob.E2ETests;

/// <summary>
/// Task 13 of the Metro restyle: the player is a flat deck with a square transport,
/// the expanded sheet is a square hairline panel and the auto-play prompt is a
/// canvas panel with an orange countdown rule.
/// </summary>
[NonParallelizable]
public class MetroPlayerTests : PageTest
{
    private const string StationName = "Metro Player Station";

    private PlaywrightWebApplicationFactory _factory = null!;
    private string _accountId = string.Empty;
    private string _profileId = string.Empty;
    private Station _station = null!;

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
            "Metro Player", $"metro-player-{Guid.NewGuid()}@test.com", "", "Test", Guid.NewGuid().ToString());
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

        // One station with two episodes, so the expanded sheet can show both nav cards.
        var slug = $"{Guid.NewGuid():N}";
        await TestCasts.ConfigureAsync(new TestCastPodcast(slug, "Metro Player Feed", new[]
        {
            new TestCastEpisode("Metro Episode Two", $"{slug}-2.mp3"),
            new TestCastEpisode("Metro Episode One", $"{slug}-1.mp3"),
        }));

        var stations = _factory.Services.GetRequiredService<StationsApiService>();
        _station = (await stations.CreateStationAsync(_accountId, new Station
        {
            AccountId = _accountId,
            Name = StationName,
            Feeds = new[] { TestCasts.FeedUrl(slug) }
        }))!;
        await profileService.AddStationToProfileAsync(_profileId, _station.Id);
        await WaitForEpisodesAsync(_station.Id, 2);
    }

    [TearDown]
    public async Task TearDown()
    {
        await Context.CloseAsync();
        _factory?.Dispose();
        TestAuthHandler.TestAccountId = string.Empty;
    }

    [Test]
    public async Task PlayerBarIsAFlatMetroDeck()
    {
        await OpenStationAsync();

        var m = await Page.EvaluateAsync<string[]>(@"() => {
            const w = document.querySelector('#ohrbob-player');
            const ws = getComputedStyle(w);
            const play = w.querySelector('.js-play');
            const ps = getComputedStyle(play);
            return [ws.position, ws.borderTopWidth, ws.borderTopStyle, ws.borderRadius,
                    ws.backdropFilter, ps.width, ps.height, ps.borderRadius, ps.backgroundColor,
                    w.classList.contains('glass-panel') ? 'glass-panel' : '',
                    w.classList.contains('glass-edge') ? 'glass-edge' : ''];
        }");

        Assert.That(m[0], Is.EqualTo("fixed"), "the deck sits on the viewport");
        Assert.That(m[1], Is.EqualTo("1px"), "the deck is separated by a 1px hairline");
        Assert.That(m[2], Is.EqualTo("solid"), "the hairline is a solid rule");
        Assert.That(ParsePx(m[3]), Is.EqualTo(0), "the deck has no radius");
        Assert.That(m[4], Is.EqualTo("none"), "the deck must not use a backdrop filter");
        Assert.That(ParsePx(m[5]), Is.EqualTo(72), "desktop play button is 72px wide");
        Assert.That(ParsePx(m[6]), Is.EqualTo(72), "desktop play button is 72px tall");
        Assert.That(ParsePx(m[7]), Is.EqualTo(0), "the play button is square");
        Assert.That(m[8], Is.EqualTo("rgb(252, 93, 0)"), "the play button is Metro orange");
        Assert.That(m[9], Is.Empty, "glass-panel must be gone from the widget");
        Assert.That(m[10], Is.Empty, "glass-edge must be gone from the widget");
    }

    [Test]
    public async Task ExpandedSheetIsSquareAndUsesTwoNavColumns()
    {
        await Page.SetViewportSizeAsync(1280, 720);
        await OpenStationAsync();
        await ExpandWithEpisodeNavAsync();

        var m = await Page.EvaluateAsync<string[]>(@"() => {
            const card = document.querySelector('.audio-expanded-card');
            const cs = getComputedStyle(card);
            const header = getComputedStyle(card.querySelector('.expanded-header-title'));
            const cover = getComputedStyle(card.querySelector('.expanded-cover, .expanded-cover-empty'));
            const row = document.querySelector('.episode-nav-row');
            const title = getComputedStyle(row.querySelector('.episode-nav-title'));
            return [cs.borderRadius, header.textTransform, header.fontSize,
                    cover.width, cover.height, cover.borderRadius,
                    getComputedStyle(row).gridTemplateColumns,
                    title.textOverflow, title.overflow, title.whiteSpace,
                    String(row.scrollWidth > row.clientWidth + 1)];
        }");

        Assert.That(ParsePx(m[0]), Is.EqualTo(0), "the expanded sheet is square");
        Assert.That(m[1], Is.EqualTo("uppercase"), "the sheet header is uppercase");
        Assert.That(ParsePx(m[2]), Is.EqualTo(11), "the sheet header is 11px");
        Assert.That(ParsePx(m[3]), Is.EqualTo(160), "the expanded cover is 160px wide");
        Assert.That(ParsePx(m[4]), Is.EqualTo(160), "the expanded cover is 160px tall");
        Assert.That(ParsePx(m[5]), Is.EqualTo(0), "the expanded cover is square");
        Assert.That(m[6].Trim().Split(' ').Length, Is.EqualTo(2),
            "previous and next episodes sit side by side on desktop");
        Assert.That(m[7], Is.EqualTo("ellipsis"), "long episode titles truncate");
        Assert.That(m[8], Is.EqualTo("hidden"), "long episode titles hide the overflow");
        Assert.That(m[9], Is.EqualTo("nowrap"), "episode titles stay on one line");
        Assert.That(m[10], Is.EqualTo("false"), "the desktop nav row must not overflow");
    }

    [Test]
    public async Task ExpandedPlayerStacksNavCardsOnPhones()
    {
        await Page.SetViewportSizeAsync(390, 844);
        await OpenStationAsync();
        await ExpandWithEpisodeNavAsync();

        await Expect(Page.Locator(".episode-nav-row .episode-nav-card")).ToHaveCountAsync(2);
        var cols = await Page.EvaluateAsync<string>(
            @"() => getComputedStyle(document.querySelector('.episode-nav-row')).gridTemplateColumns");
        Assert.That(cols.Trim().Split(' ').Length, Is.EqualTo(1), "cards must stack on phones");

        var m = await Page.EvaluateAsync<string[]>(@"() => {
            const row = document.querySelector('.episode-nav-row');
            const title = row.querySelector('.episode-nav-title');
            const body = getComputedStyle(document.querySelector('.audio-expanded-card .expanded-body'));
            return [String(row.scrollWidth > row.clientWidth + 1),
                    String(title.scrollWidth > title.clientWidth + 1),
                    getComputedStyle(title).textOverflow, body.paddingLeft];
        }");

        Assert.That(m[0], Is.EqualTo("false"), "the stacked nav row must not overflow");
        Assert.That(m[1], Is.EqualTo("true"), "the long previous/next title is actually truncated");
        Assert.That(m[2], Is.EqualTo("ellipsis"), "the truncated title uses an ellipsis");
        Assert.That(ParsePx(m[3]), Is.EqualTo(16), "the expanded sheet uses tighter phone padding");
    }

    [Test]
    public async Task MobilePlayerUsesCompactTransport()
    {
        await Page.SetViewportSizeAsync(390, 844);
        await OpenStationAsync();

        var m = await Page.EvaluateAsync<string[]>(@"() => {
            const w = document.querySelector('#ohrbob-player');
            const play = getComputedStyle(w.querySelector('.js-play'));
            const skip = getComputedStyle(w.querySelector('.js-skipback'));
            const volume = getComputedStyle(w.querySelector('.audio-volume-group'));
            const progress = getComputedStyle(w.querySelector('.audio-progress-group'));
            const menu = getComputedStyle(w.querySelector('.btn-menu'));
            return [play.width, play.height, skip.width, skip.height,
                    volume.display, progress.display, menu.display];
        }");

        Assert.That(ParsePx(m[0]), Is.EqualTo(52), "the phone play button is 52px wide");
        Assert.That(ParsePx(m[1]), Is.EqualTo(52), "the phone play button is 52px tall");
        Assert.That(ParsePx(m[2]), Is.EqualTo(34), "the phone skip button is 34px wide");
        Assert.That(ParsePx(m[3]), Is.EqualTo(34), "the phone skip button is 34px tall");
        Assert.That(m[4], Is.EqualTo("none"), "volume is hidden on phones");
        Assert.That(m[5], Is.EqualTo("none"), "the progress group is hidden on phones");
        Assert.That(m[6], Is.EqualTo("none"), "the menu is hidden on phones");
    }

    [Test]
    public async Task AutoStartPromptUsesTheMetroPanel()
    {
        await Page.SetViewportSizeAsync(1280, 900);
        await OpenStationAsync();

        // A reload is an automatic start, so the countdown prompt appears first.
        await Page.ReloadAsync();
        var overlay = Page.Locator("#ohrbob-auto-start.show");
        await Expect(overlay).ToBeVisibleAsync(new() { Timeout = 20000 });

        var m = await Page.EvaluateAsync<string[]>(@"() => {
            const el = document.getElementById('ohrbob-auto-start');
            const es = getComputedStyle(el);
            const fill = getComputedStyle(el.querySelector('.ohrbob-auto-start-bar-fill'));
            const text = getComputedStyle(el.querySelector('.ohrbob-auto-start-text'));
            const button = getComputedStyle(el.querySelector('.js-auto-start-play'));
            return [el.classList.contains('metro-auto-start') ? 'yes' : 'no',
                    es.position, es.borderTopWidth, es.borderTopStyle, es.borderRadius,
                    es.backgroundColor, fill.height, fill.backgroundColor,
                    text.textTransform, text.fontSize, button.borderRadius];
        }");

        Assert.That(m[0], Is.EqualTo("yes"), "the prompt carries the metro-auto-start hook");
        Assert.That(m[1], Is.EqualTo("fixed"), "the prompt floats above the deck");
        Assert.That(m[2], Is.EqualTo("1px"), "the prompt has a hairline border");
        Assert.That(m[3], Is.EqualTo("solid"), "the prompt border is a solid rule");
        Assert.That(ParsePx(m[4]), Is.EqualTo(0), "the prompt is square");
        Assert.That(m[5], Is.EqualTo("rgb(244, 241, 234)"), "the prompt sits on the canvas");
        Assert.That(ParsePx(m[6]), Is.EqualTo(3), "the countdown rule is 3px tall");
        Assert.That(m[7], Is.EqualTo("rgb(252, 93, 0)"), "the countdown rule is Metro orange");
        Assert.That(m[8], Is.EqualTo("uppercase"), "the prompt label is uppercase");
        Assert.That(ParsePx(m[9]), Is.EqualTo(11), "the prompt label is 11px");
        Assert.That(ParsePx(m[10]), Is.EqualTo(0), "the prompt buttons are square");

        var rects = await Page.EvaluateAsync<double[]>(@"() => {
            const prompt = document.getElementById('ohrbob-auto-start').getBoundingClientRect();
            const deck = document.getElementById('ohrbob-player').getBoundingClientRect();
            return [prompt.bottom, deck.top];
        }");
        Assert.That(rects[0], Is.LessThanOrEqualTo(rects[1]),
            "the desktop prompt must sit above the transport deck without covering it");
    }

    [Test]
    public async Task ToastContainerClearsThePlayerDeck()
    {
        await Page.SetViewportSizeAsync(1280, 900);
        await OpenStationAsync();

        var desktop = await Page.EvaluateAsync<double[]>(@"() => {
            const deck = document.querySelector('#ohrbob-player');
            const toast = document.querySelector('.app-toast-container');
            return [deck.getBoundingClientRect().height,
                    parseFloat(getComputedStyle(toast).bottom),
                    window.innerHeight - toast.getBoundingClientRect().bottom];
        }");
        Assert.That(desktop[1], Is.GreaterThanOrEqualTo(desktop[0] + 8),
            "toasts must clear the desktop player deck");
        Assert.That(desktop[2], Is.GreaterThanOrEqualTo(desktop[0] + 8),
            "the toast container sits above the deck, not on top of it");

        await Page.SetViewportSizeAsync(390, 844);
        var mobile = await Page.EvaluateAsync<double[]>(@"() => {
            const deck = document.querySelector('#ohrbob-player');
            const toast = document.querySelector('.app-toast-container');
            return [deck.getBoundingClientRect().height,
                    parseFloat(getComputedStyle(toast).bottom),
                    window.innerHeight - toast.getBoundingClientRect().bottom];
        }");
        Assert.That(mobile[1], Is.GreaterThanOrEqualTo(mobile[0] + 8),
            "toasts must clear the mobile player deck");
        Assert.That(mobile[1], Is.EqualTo(90), "the mobile toast offset is 90 px");
    }

    private async Task OpenStationAsync()
    {
        await Page.GotoAsync(_factory.ServerAddress);
        var card = Page.Locator(".card-station", new PageLocatorOptions { HasTextString = StationName }).First;
        await card.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20000 });
        await card.Locator(".card-station-play").ClickAsync();

        await Expect(Page.Locator("#ohrbob-player .audio-bar")).ToBeVisibleAsync(new() { Timeout = 20000 });
        await Expect(Page.Locator("#ohrbob-player")).Not.ToHaveAttributeAsync(
            "track", "Track Name", new() { Timeout = 30000 });
    }

    private async Task ExpandWithEpisodeNavAsync()
    {
        // Wait for the initial server sync to land, then stop the 5 s queue refresh: the
        // test pins both nav cards even though this two-episode station only has one
        // neighbour from the player's point of view.
        await Page.WaitForFunctionAsync(@"() => {
            const w = document.getElementById('ohrbob-player');
            return !!w && !!(w.getAttribute('next-episode') || w.getAttribute('prev-episode'));
        }", null, new() { Timeout = 20000 });
        // The sync id is assigned by player-integration after load; wait for it so the
        // clear below cannot silently no-op and let the 5 s refresh overwrite the pinned cards.
        await Page.WaitForFunctionAsync("() => !!window._ohrbobSyncInterval", null,
            new() { Timeout = 10000 });
        await Page.EvaluateAsync(@"() => {
            if (window._ohrbobSyncInterval) clearInterval(window._ohrbobSyncInterval);
            const w = document.getElementById('ohrbob-player');
            w.setAttribute('prev-episode', JSON.stringify({ track: 'A very long previous episode title that should truncate', artist: 'Feed' }));
            w.setAttribute('next-episode', JSON.stringify({ track: 'A very long next episode title that should truncate', artist: 'Feed' }));
            w.isExpanded = true;
        }");
        await Expect(Page.Locator(".episode-nav-row .episode-nav-card")).ToHaveCountAsync(2);
        await Page.WaitForTimeoutAsync(400);
    }

    private static double ParsePx(string value) =>
        double.Parse(value.Replace("px", string.Empty), System.Globalization.CultureInfo.InvariantCulture);

    private async Task WaitForEpisodesAsync(string stationId, int minimum)
    {
        var stations = _factory.Services.GetRequiredService<StationsApiService>();
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var details = await stations.GetStationDetailsByIdAsync(stationId);
            if ((details?.Feeds?.Sum(f => f.EpisodeCount) ?? 0) >= minimum) return;
            await Task.Delay(500);
        }
        Assert.Fail($"Feed episodes were not parsed within the timeout (station {stationId}).");
    }

    private async Task InstallMockAudioAsync()
    {
        // Deterministic media: an Audio replacement keeps playback fast and independent of
        // the testcasts host being reachable from the browser.
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
                        if (this['on' + type]) this['on' + type](event);
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
                        setActionHandler: function(action, callback) {
                            if (!this._handlers) this._handlers = {};
                            this._handlers[action] = callback;
                        },
                        setPositionState: function(state) { this._positionState = state; }
                    };
                }
            })();
        ");
    }
}
