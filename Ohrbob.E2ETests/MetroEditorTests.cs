using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using Ohrbob.E2ETests.Infrastructure;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Services;

namespace Ohrbob.E2ETests;

/// <summary>
/// Task 6 of the Metro restyle: the station editor is a flat sticky canvas bar with
/// underline tabs, two canvas panes, and feed/episode rows that carry their feed colour.
/// </summary>
[NonParallelizable]
public class MetroEditorTests : PageTest
{
    private PlaywrightWebApplicationFactory _factory = null!;
    private string _accountId = string.Empty;
    private string _profileId = string.Empty;
    private string _stationId = string.Empty;

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        IgnoreHTTPSErrors = true,
        ViewportSize = new ViewportSize { Width = 1280, Height = 900 }
    };

    [SetUp]
    public async Task SetUp()
    {
        await Context.ClearCookiesAsync();

        if (!await TestCasts.IsAvailableAsync())
        {
            Assert.Ignore($"podcast-testcasts is not reachable at {TestCasts.ConfigureBaseUrl}.");
        }

        _factory = new PlaywrightWebApplicationFactory();
        Page.Console += (_, msg) => Console.WriteLine($"BROWSER CONSOLE: {msg.Text}");
        Page.PageError += (_, err) => Console.WriteLine($"BROWSER ERROR: {err}");

        var accountService = _factory.Services.GetRequiredService<AccountApiService>();
        var account = await accountService.CreateOrUpdateAccountAsync(
            "Metro Editor", $"metro-editor-{Guid.NewGuid()}@test.com", "", "Test", Guid.NewGuid().ToString());
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

        var slug = $"metro-editor-{Guid.NewGuid():N}";
        await TestCasts.ConfigureAsync(new TestCastPodcast(slug, "Metro Editor Feed", new[]
        {
            new TestCastEpisode("Metro Editor Episode", $"{slug}-1.mp3"),
        }));

        var stations = _factory.Services.GetRequiredService<StationsApiService>();
        var station = await stations.CreateStationAsync(_accountId, new Station
        {
            AccountId = _accountId,
            Name = "Metro Editor Station",
            Feeds = new[] { TestCasts.FeedUrl(slug) }
        });
        _stationId = station!.Id;
        await profileService.AddStationToProfileAsync(_profileId, _stationId);
        await WaitForEpisodesAsync(_stationId, 1);

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
    public async Task EditorHeaderAndTabsAreFlat()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Stations/Edit/{_stationId}");
        var m = await Page.EvaluateAsync<string[]>(@"() => {
            const header = document.querySelector('#dockable-header');
            const h = getComputedStyle(header);
            const t = getComputedStyle(document.querySelector('#rotation-tab'));
            header.classList.add('docked');
            const docked = getComputedStyle(header);
            const dockedMetrics = [docked.top, docked.boxShadow, docked.borderRadius];
            header.classList.remove('docked');
            return [h.backdropFilter, h.backgroundColor, h.borderBottomWidth,
                    t.borderBottomWidth, t.borderBottomColor,
                    h.boxShadow, h.borderRadius, ...dockedMetrics];
        }");
        Assert.That(m[0], Is.EqualTo("none"));
        Assert.That(m[1], Is.Not.EqualTo("rgba(0, 0, 0, 0)"), "the bar sits on the canvas");
        Assert.That(m[2], Is.EqualTo("1px"), "the bar has one hairline");
        Assert.That(m[3], Is.EqualTo("2px"));
        Assert.That(m[4], Does.Match("rgb\\(252, 93, 0\\)"));
        Assert.That(m[5], Is.EqualTo("none"), "no shadow");
        Assert.That(m[6], Is.EqualTo("0px"), "no radius");
        Assert.That(m[7], Is.EqualTo("62px"), "the docked offset is preserved");
        Assert.That(m[8], Is.EqualTo("none"), "the docked bar stays flat");
        Assert.That(m[9], Is.EqualTo("0px"), "the docked bar stays square");
    }

    [Test]
    public async Task FeedAndEpisodeRowsCarryFeedColours()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Stations/Edit/{_stationId}");
        var feed = await Page.EvaluateAsync<string>(
            @"() => getComputedStyle(document.querySelector('.card-station-feed')).borderLeftWidth");
        Assert.That(feed, Is.EqualTo("4px"));

        await Page.SetViewportSizeAsync(375, 812);
        await Page.ClickAsync("#resolved-tab");
        var bar = await Page.EvaluateAsync<string>(@"() => {
            const el = document.querySelector('#resolvedList .card-episode');
            return getComputedStyle(el, '::before').backgroundColor;
        }");
        Assert.That(bar, Is.Not.EqualTo("rgba(0, 0, 0, 0)"), "non-playing rows must still show a feed bar");

        // A completed row keeps its feed bar too (the old markup dropped the accent var
        // for completed episodes, so the bar fell back to transparent).
        var stations = _factory.Services.GetRequiredService<StationsApiService>();
        var playlist = await stations.GetStationPlaylistAsync(_stationId, _profileId);
        await stations.MarkEpisodeCompletedAsync(_stationId, playlist!.First().Episode.Id, _profileId);

        await Page.GotoAsync($"{_factory.ServerAddress}/Stations/Edit/{_stationId}");
        await Page.SetViewportSizeAsync(375, 812);
        await Page.ClickAsync("#resolved-tab");
        var completed = await Page.EvaluateAsync<string[]>(@"() => {
            const el = document.querySelector('#resolvedList .card-episode.is-played');
            if (!el) return [];
            const feedColor = el.getAttribute('data-feed-color');
            const probe = document.createElement('span');
            probe.style.backgroundColor = feedColor;
            document.body.appendChild(probe);
            const expected = getComputedStyle(probe).backgroundColor;
            probe.remove();
            return [getComputedStyle(el, '::before').backgroundColor,
                    expected,
                    el.getAttribute('style') || ''];
        }");
        Assert.That(completed, Has.Length.EqualTo(3), "expected a completed episode row");
        Assert.That(completed[0], Is.Not.EqualTo("rgba(0, 0, 0, 0)"), "completed rows keep their feed bar");
        Assert.That(completed[0], Is.EqualTo(completed[1]), "completed rows keep their feed colour, not the default accent");
        Assert.That(completed[2], Does.Contain("--card-episode-accent"), "every row sets the accent inline");
    }

    [Test]
    public async Task EditorRowsUseMetroGeometry()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Stations/Edit/{_stationId}");
        await Expect(Page.Locator("#rotationContainer .card-station-feed")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(Page.Locator("#resolvedList .card-episode.resolved-item")).ToBeVisibleAsync(new() { Timeout = 15000 });

        var feed = await Page.EvaluateAsync<double[]>(@"() => {
            const row = document.querySelector('#rotationContainer .card-station-feed');
            const cover = row.querySelector('.card-station-feed-cover');
            const title = row.querySelector('.card-station-feed-title');
            const meta = row.querySelector('.card-station-feed-meta');
            const toggle = row.querySelector('.playback-order-toggle');
            const span = row.querySelector('.episodes-input');
            const menu = row.querySelector('.feed-actions-menu');
            return [parseFloat(getComputedStyle(cover).width),
                    parseFloat(getComputedStyle(title).fontSize),
                    parseFloat(getComputedStyle(meta).fontSize),
                    parseFloat(getComputedStyle(toggle).borderRadius),
                    parseFloat(getComputedStyle(span).width),
                    parseFloat(getComputedStyle(menu).width)];
        }");
        Assert.That(feed[0], Is.EqualTo(96), "feed cover");
        Assert.That(feed[1], Is.EqualTo(15), "feed title");
        Assert.That(feed[2], Is.EqualTo(11), "feed meta");
        Assert.That(feed[3], Is.EqualTo(0), "the rotation toggle must be square");
        Assert.That(feed[4], Is.EqualTo(44), "span input");
        Assert.That(feed[5], Is.EqualTo(30), "feed menu");

        var episode = await Page.EvaluateAsync<string[]>(@"() => {
            const row = document.querySelector('#resolvedList .card-episode.resolved-item');
            const cover = row.querySelector('.card-episode-cover, .card-episode-badge');
            const title = row.querySelector('.card-episode-title');
            const artist = row.querySelector('.card-episode-artist');
            const date = row.querySelector('.card-episode-date');
            const bar = getComputedStyle(row, '::before');
            const play = row.querySelector('.card-episode-play');
            const playStyle = getComputedStyle(play);
            return [getComputedStyle(cover).width,
                    getComputedStyle(title).fontSize,
                    getComputedStyle(title).fontWeight,
                    getComputedStyle(artist).fontSize,
                    getComputedStyle(date).fontSize,
                    bar.width,
                    playStyle.width,
                    playStyle.height,
                    playStyle.backgroundColor];
        }");
        static double Px(string value) => double.Parse(value.Replace("px", ""), System.Globalization.CultureInfo.InvariantCulture);
        Assert.That(Px(episode[0]), Is.EqualTo(58), "episode cover");
        Assert.That(Px(episode[1]), Is.EqualTo(15), "episode title");
        Assert.That(int.Parse(episode[2], System.Globalization.CultureInfo.InvariantCulture), Is.GreaterThanOrEqualTo(600), "episode title weight");
        Assert.That(Px(episode[3]), Is.EqualTo(11), "episode artist");
        Assert.That(Px(episode[4]), Is.EqualTo(10), "episode date");
        Assert.That(Px(episode[5]), Is.EqualTo(4), "episode feed bar");
        Assert.That(Px(episode[6]), Is.EqualTo(40), "play target");
        Assert.That(Px(episode[7]), Is.EqualTo(40), "play target");
        Assert.That(episode[8], Does.Match("rgb\\(252, 93, 0\\)"), "play square is orange");
    }

    [Test]
    public async Task EditorPanesHaveAHairlineDivider()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Stations/Edit/{_stationId}");
        var m = await Page.EvaluateAsync<double[]>(@"() => {
            const panes = Array.from(document.querySelectorAll('#stationTabsContent > .editor-pane'));
            return [panes.length,
                    parseFloat(getComputedStyle(panes[1]).borderLeftWidth),
                    panes[0].getBoundingClientRect().width,
                    panes[1].getBoundingClientRect().width];
        }");
        Assert.That(m[0], Is.EqualTo(2), "editor panes");
        Assert.That(m[1], Is.EqualTo(1), "pane divider");
        Assert.That(Math.Abs(m[2] - m[3]), Is.LessThan(2), "the two panes sit side by side");

        // Every modal uses the Metro shell (accent bar, square canvas surface).
        var modal = await Page.EvaluateAsync<string[]>(@"() => {
            const modals = Array.from(document.querySelectorAll('#main-content .modal'));
            const shells = modals.filter((m) => m.querySelector('.modal-content > .metro-modal-accent'));
            const content = document.querySelector('#renameStationModal .modal-content');
            const accent = content.querySelector('.metro-modal-accent');
            return [getComputedStyle(content).borderRadius,
                    accent ? getComputedStyle(accent).height : 'missing',
                    accent ? getComputedStyle(accent).backgroundColor : 'missing',
                    modals.length.toString(), shells.length.toString()];
        }");
        Assert.That(modal[0], Is.EqualTo("0px"), "modal is square");
        Assert.That(modal[1], Is.EqualTo("6px"), "modal accent bar");
        Assert.That(modal[2], Does.Match("rgb\\(252, 93, 0\\)"), "modal accent is orange");
        Assert.That(modal[4], Is.EqualTo(modal[3]), "every editor modal has the Metro accent bar");
    }

    [Test]
    public async Task EditorModalChromeIsScopedAndFocusIsVisible()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Stations/Edit/{_stationId}");
        await Expect(Page.Locator("#renameStationModal")).ToHaveCountAsync(1, new() { Timeout = 15000 });

        var styles = await Page.EvaluateAsync<string[]>(@"() => {
            const editorTitle = getComputedStyle(document.querySelector('#renameStationModal .modal-title'));
            const globalTitle = getComputedStyle(document.querySelector('#confirmationModal .modal-title'));
            const editorBody = getComputedStyle(document.querySelector('#renameStationModal .modal-body'));
            const globalBody = getComputedStyle(document.querySelector('#confirmationModalBody'));
            const field = document.getElementById('shareStationUrl');
            field.focus();
            const focused = getComputedStyle(field);
            return [editorTitle.fontFamily, editorTitle.fontSize, globalTitle.fontSize,
                    editorBody.fontSize, globalBody.fontSize,
                    focused.outlineWidth, focused.outlineStyle, focused.outlineColor];
        }");
        Assert.That(styles[0], Does.Contain("Sulphur"), "editor modal titles use the display face");
        Assert.That(styles[1], Is.EqualTo("24px"), "editor modal title size");
        Assert.That(styles[2], Is.EqualTo("17.5px"), "global modal title keeps its size");
        Assert.That(styles[3], Is.EqualTo("13px"), "editor modal body size");
        Assert.That(styles[4], Is.EqualTo("14px"), "global modal body keeps its size");
        Assert.That(styles[5], Is.EqualTo("2px"), "share field focus ring width");
        Assert.That(styles[6], Is.EqualTo("solid"), "share field focus ring is drawn");
        Assert.That(styles[7], Does.Match("rgb\\(252, 93, 0\\)"), "share field focus ring is orange");
    }

    [Test]
    public async Task EditorFitsANarrowViewport()
    {
        await Page.SetViewportSizeAsync(375, 812);
        await Page.GotoAsync($"{_factory.ServerAddress}/Stations/Edit/{_stationId}");
        await Expect(Page.Locator("#rotation-tab")).ToBeVisibleAsync(new() { Timeout = 15000 });

        var rotation = await OverflowAsync();
        Assert.That(rotation, Is.Empty, "the rotation pane must fit 375 px:\n  " + string.Join("\n  ", rotation));

        await Page.ClickAsync("#resolved-tab");
        await Expect(Page.Locator("#resolvedList .card-episode")).ToBeVisibleAsync(new() { Timeout = 15000 });
        var episodes = await OverflowAsync();
        Assert.That(episodes, Is.Empty, "the playlist pane must fit 375 px:\n  " + string.Join("\n  ", episodes));

        await Page.SetViewportSizeAsync(390, 844);
        await Page.ReloadAsync();
        await Expect(Page.Locator("#rotation-tab")).ToBeVisibleAsync(new() { Timeout = 15000 });
        var wider = await OverflowAsync();
        Assert.That(wider, Is.Empty, "the editor must fit 390 px:\n  " + string.Join("\n  ", wider));
    }

    private async Task<string[]> OverflowAsync()
    {
        var values = await Page.EvaluateAsync<string[]>(@"() => {
            const vw = document.documentElement.clientWidth;
            const offenders = [];
            if (document.documentElement.scrollWidth > vw + 1) {
                offenders.push('page scrollWidth ' + document.documentElement.scrollWidth + ' > ' + vw);
            }
            document.querySelectorAll('#main-content *').forEach(el => {
                const r = el.getBoundingClientRect();
                if (r.width > 0 && r.right > vw + 1) {
                    const cls = typeof el.className === 'string' ? el.className : '';
                    offenders.push(el.tagName + '.' + cls + ' right=' + Math.round(r.right) + ' vw=' + vw);
                }
            });
            return offenders.slice(0, 8);
        }");
        return values;
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
}
