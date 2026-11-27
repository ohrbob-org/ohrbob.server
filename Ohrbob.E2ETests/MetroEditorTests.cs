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
[Category("TestCasts")]
[NonParallelizable]
public class MetroEditorTests : E2EPageTest
{
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

        SetAccount(_accountId);
        _factory.CreateClient();
    }

    [TearDown]
    public async Task TearDown()
    {
        await Context.CloseAsync();
        SetAccount(string.Empty);
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
        Assert.That(m[2], Is.EqualTo("0px"), "the bar has no rule under the station title");
        Assert.That(m[3], Is.EqualTo("2px"));
        Assert.That(m[4], Does.Match("rgb\\(252, 93, 0\\)"));
        Assert.That(m[5], Is.EqualTo("none"), "no shadow");
        Assert.That(m[6], Is.EqualTo("0px"), "no radius");
        Assert.That(m[7], Is.EqualTo("0px"), "the docked bar sticks to the top of the viewport");
        Assert.That(m[8], Is.EqualTo("none"), "the docked bar stays flat");
        Assert.That(m[9], Is.EqualTo("0px"), "the docked bar stays square");
    }

    [Test]
    public async Task PaneTitlesMatchAndPlaylistRowsKeepTheirHeight()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Stations/Edit/{_stationId}");

        var json = await Page.EvaluateAsync<string>(@"() => {
            const titles = Array.from(document.querySelectorAll('.editor-pane__title'))
                .filter(e => getComputedStyle(e).display !== 'none')
                .map(e => ({ text: e.textContent.trim(), size: parseFloat(getComputedStyle(e).fontSize),
                             family: getComputedStyle(e).fontFamily }));
            const rows = Array.from(document.querySelectorAll('#resolvedList .card-episode'))
                .map(e => ({ h: e.getBoundingClientRect().height,
                             border: parseFloat(getComputedStyle(e).borderTopWidth),
                             left: parseFloat(getComputedStyle(e).borderLeftWidth),
                             body: e.querySelector('.card-episode-body').getBoundingClientRect().width,
                             mb: parseFloat(getComputedStyle(e).marginBottom) }));
            return JSON.stringify({ titles, rows });
        }");
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var titles = doc.RootElement.GetProperty("titles");
        var texts = titles.EnumerateArray().Select(t => t.GetProperty("text").GetString()).ToArray();
        var sizes = titles.EnumerateArray().Select(t => t.GetProperty("size").GetDouble()).ToArray();
        Assert.That(texts, Does.Contain("Podcasts"));
        Assert.That(texts, Does.Contain("Playlist"), "the episode pane is called Playlist now");
        Assert.That(sizes.Distinct().Count(), Is.EqualTo(1), "both pane titles use the same size");
        Assert.That(sizes[0], Is.GreaterThanOrEqualTo(24), "pane titles are larger than the old 11 px labels");

        var rows = doc.RootElement.GetProperty("rows").EnumerateArray().Select(r => new
        {
            Height = r.GetProperty("h").GetDouble(),
            Border = r.GetProperty("border").GetDouble(),
            Left = r.GetProperty("left").GetDouble(),
            Body = r.GetProperty("body").GetDouble(),
            Margin = r.GetProperty("mb").GetDouble(),
        }).ToArray();
        Assert.That(rows, Is.Not.Empty, "the playlist has episodes to measure");
        Assert.That(rows.Select(r => r.Height), Has.All.GreaterThanOrEqualTo(58),
            "playlist rows keep the content height");
        Assert.That(rows.Select(r => r.Border), Has.All.EqualTo(0),
            "there is no horizontal rule between episodes");
        Assert.That(rows.Select(r => r.Left), Has.All.EqualTo(4),
            "each row carries a full-height 4 px feed border");
        Assert.That(rows.Select(r => r.Body), Has.All.GreaterThan(200),
            "the episode text keeps the main column instead of collapsing");
        Assert.That(rows.Take(rows.Length - 1).Select(r => r.Margin), Has.All.EqualTo(8),
            "playlist entries use the same 8 px gap as the feed cards");
    }

    [Test]
    public async Task EditorHeaderOffersABackLinkToListen()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Stations/Edit/{_stationId}");
        var back = Page.Locator("#dockable-header .editor-back-btn");
        await Expect(back).ToBeVisibleAsync(new() { Timeout = 15000 });
        Assert.That(await back.GetAttributeAsync("href"), Is.EqualTo("/"));
        Assert.That(await back.GetAttributeAsync("aria-label"), Is.EqualTo("Back to listen"));

        // The gear sits between the back arrow and the station title.
        var gear = Page.Locator("#dockable-header #renameStationBtn");
        await Expect(gear).ToBeVisibleAsync();
        var order = await Page.EvaluateAsync<double[]>(
            @"() => { const r = (s) => document.querySelector(s).getBoundingClientRect();
                      const b = r('.editor-back-btn'), g = r('#renameStationBtn'), t = r('#stationNameDisplay');
                      return [b.left, g.left, t.left]; }");
        Assert.That(order[0], Is.LessThan(order[1]), "the gear follows the back arrow");
        Assert.That(order[1], Is.LessThan(order[2]), "the title follows the gear");

        await back.ClickAsync();
        await Page.WaitForURLAsync(new System.Text.RegularExpressions.Regex("/$"), new() { Timeout = 15000 });
        await Expect(Page.Locator(".page-head .metro-title")).ToContainTextAsync("listen");
    }

    [Test]
    public async Task FeedAndEpisodeRowsCarryFeedColours()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Stations/Edit/{_stationId}");
        var feed = await Page.EvaluateAsync<string[]>(
            @"() => { const el = document.querySelector('.card-station-feed');
                     const s = getComputedStyle(el);
                     const accent = getComputedStyle(el).getPropertyValue('--station-feed-accent').trim();
                     const probe = document.createElement('span');
                     probe.style.backgroundColor = accent;
                     document.body.appendChild(probe);
                     const expected = getComputedStyle(probe).backgroundColor;
                     probe.remove();
                     return [s.borderLeftWidth, s.borderTopWidth, s.backgroundColor, expected]; }");
        Assert.That(feed[0], Is.EqualTo("4px"), "the feed bar stays 4 px");
        Assert.That(feed[1], Is.EqualTo("0px"), "feed cards have no outline");
        Assert.That(feed[2], Is.EqualTo(feed[3]), "feed cards are filled with the feed colour");

        var placeholder = await Page.EvaluateAsync<string[]>(@"() => {
            const el = document.querySelector('.card-station-feed-cover-empty');
            const s = getComputedStyle(el);
            return [s.fontSize, s.backgroundColor];
        }");
        Assert.That(placeholder, Has.Length.EqualTo(2), "the feed without cover art renders an initial");
        Assert.That(double.Parse(placeholder[0].Replace("px", string.Empty),
            System.Globalization.CultureInfo.InvariantCulture), Is.GreaterThanOrEqualTo(40),
            "the feed initial is large");
        Assert.That(placeholder[1], Is.Not.EqualTo("rgb(244, 241, 234)"),
            "the placeholder is not a white/paper square");

        await Page.SetViewportSizeAsync(375, 812);
        await Page.ClickAsync("#resolved-tab");
        var bar = await Page.EvaluateAsync<string[]>(@"() => {
            const el = document.querySelector('#resolvedList .card-episode');
            const s = getComputedStyle(el);
            return [s.borderLeftWidth, s.borderLeftColor];
        }");
        Assert.That(bar[0], Is.EqualTo("4px"), "non-playing rows show a 4 px feed border");
        Assert.That(bar[1], Is.Not.EqualTo("rgba(0, 0, 0, 0)"), "the feed border has the feed colour");

        var episodePlaceholder = await Page.EvaluateAsync<string>(@"() => {
            const el = document.querySelector('#resolvedList .card-episode-badge');
            return el ? getComputedStyle(el).backgroundColor : 'missing';
        }");
        Assert.That(episodePlaceholder, Is.Not.EqualTo("rgb(244, 241, 234)"),
            "episode placeholders are not white tiles either");

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
            return [getComputedStyle(el).backgroundColor,
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
            const backdrop = row.querySelector('.card-episode-backdrop');
            const title = row.querySelector('.card-episode-title');
            const artist = row.querySelector('.card-episode-artist');
            const date = row.querySelector('.card-episode-date');
            const bar = getComputedStyle(row).borderLeftWidth;
            const play = row.querySelector('.card-episode-play');
            const playStyle = getComputedStyle(play);
            const backdropStyle = getComputedStyle(backdrop);
            return [backdropStyle.position, parseFloat(backdropStyle.opacity).toString(),
                    getComputedStyle(title).fontSize,
                    getComputedStyle(title).fontWeight,
                    getComputedStyle(artist).fontSize,
                    getComputedStyle(date).fontSize,
                    bar,
                    playStyle.width,
                    playStyle.height,
                    playStyle.backgroundColor,
                    getComputedStyle(row).backgroundColor];
        }");
        static double Px(string value) => double.Parse(value.Replace("px", ""), System.Globalization.CultureInfo.InvariantCulture);
        Assert.That(episode[0], Is.EqualTo("absolute"), "the cover is a background layer");
        Assert.That(double.Parse(episode[1], System.Globalization.CultureInfo.InvariantCulture),
            Is.LessThan(1), "the cover is semi-transparent");
        Assert.That(Px(episode[2]), Is.EqualTo(15), "episode title");
        Assert.That(int.Parse(episode[3], System.Globalization.CultureInfo.InvariantCulture), Is.GreaterThanOrEqualTo(600), "episode title weight");
        Assert.That(Px(episode[4]), Is.EqualTo(11), "episode artist");
        Assert.That(Px(episode[5]), Is.EqualTo(10), "episode date");
        Assert.That(Px(episode[6]), Is.EqualTo(4), "episode feed bar");
        Assert.That(Px(episode[7]), Is.EqualTo(40), "play target");
        Assert.That(Px(episode[8]), Is.EqualTo(40), "play target");
        Assert.That(episode[9], Is.Not.EqualTo("rgba(0, 0, 0, 0)"), "play square is filled");
        Assert.That(episode[9], Is.Not.EqualTo(episode[10]), "play square contrasts with the feed tile");

        // Spec 9: fine pointers keep the compact icons and must not rely on
        // overlapping pseudo-element hit boxes that steal a neighbour's click.
        var targets = await Page.EvaluateAsync<string[]>(@"() => {
            const mini = document.querySelector('.episode-mini-btn');
            const action = document.querySelector('.episode-action-btn');
            const after = (el) => getComputedStyle(el, '::after').content;
            return [getComputedStyle(mini).width, getComputedStyle(action).width,
                    after(mini), after(action)];
        }");
        Assert.That(targets[0], Is.EqualTo("26px"), "the mini icon stays 26 px on fine pointers");
        Assert.That(targets[1], Is.EqualTo("28px"), "the action icon stays 28 px on fine pointers");
        Assert.That(targets[2], Is.EqualTo("none"), "mini controls have no overlapping ::after hit box");
        Assert.That(targets[3], Is.EqualTo("none"), "action controls have no overlapping ::after hit box");
    }

    [Test]
    public async Task EditorEpisodeControlsAreSeparateTouchTargets()
    {
        await using var context = await Browser.NewContextAsync(new()
        {
            IgnoreHTTPSErrors = true,
            HasTouch = true,
            IsMobile = true,
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        var page = await context.NewPageAsync();
        var requests = new List<string>();
        page.Request += (_, request) => requests.Add(request.Url);

        await page.GotoAsync($"{_factory.ServerAddress}/Stations/Edit/{_stationId}");
        await page.ClickAsync("#resolved-tab");
        await Expect(page.Locator("#resolvedList .card-episode .episode-mini-btn").First)
            .ToBeVisibleAsync(new() { Timeout = 15000 });

        static double OverlapArea(double[] a, double[] b)
        {
            var width = Math.Min(a[2], b[2]) - Math.Max(a[0], b[0]);
            var height = Math.Min(a[3], b[3]) - Math.Max(a[1], b[1]);
            return Math.Max(0, width) * Math.Max(0, height);
        }

        static void AssertTouchTargets(double[][] boxes, string label)
        {
            Assert.That(boxes, Has.Length.EqualTo(2), $"{label} pair");
            foreach (var box in boxes)
            {
                Assert.That(box[4], Is.GreaterThanOrEqualTo(44), $"{label} width");
                Assert.That(box[5], Is.GreaterThanOrEqualTo(44), $"{label} height");
            }
            Assert.That(OverlapArea(boxes[0], boxes[1]), Is.EqualTo(0),
                $"{label} buttons must not overlap");
            var span = Math.Max(boxes[0][2], boxes[1][2]) - Math.Min(boxes[0][0], boxes[1][0]);
            Assert.That(span, Is.LessThanOrEqualTo(88 + 0.5), $"{label} pair stays at most 88 px wide");
        }

        var miniBoxes = await page.EvaluateAsync<double[][]>(@"() => {
            const row = document.querySelector('#resolvedList .card-episode');
            row.scrollIntoView({ block: 'center', behavior: 'instant' });
            const rect = (el) => {
                const r = el.getBoundingClientRect();
                return [r.left, r.top, r.right, r.bottom, r.width, r.height];
            };
            return Array.from(row.querySelectorAll('.episode-mini-btn')).map(rect);
        }");
        AssertTouchTargets(miniBoxes, "playhead");

        // The outer right edge of reset must hit reset, not the set button.
        requests.Clear();
        await page.Mouse.ClickAsync((float)(miniBoxes[0][0] + miniBoxes[0][4] - 2), (float)(miniBoxes[0][1] + miniBoxes[0][5] / 2));
        await page.WaitForTimeoutAsync(500);
        await Expect(page.Locator("#setPlayheadModal")).Not.ToBeVisibleAsync();
        Assert.That(requests, Has.Some.Contains("/Stations/SetPlayhead"),
            "the reset button handled the edge click");
        Assert.That(await page.Locator("#setPlayheadModal.show").CountAsync(), Is.EqualTo(0),
            "the set-playhead modal must not open from the reset button's edge");

        await Expect(page.Locator("#resolvedList .mark-completed-btn").First)
            .ToBeVisibleAsync(new() { Timeout = 15000 });
        var actionBoxes = await page.EvaluateAsync<double[][]>(@"() => {
            const row = Array.from(document.querySelectorAll('#resolvedList .card-episode'))
                .find((r) => r.querySelector('.mark-completed-btn'));
            row.scrollIntoView({ block: 'center', behavior: 'instant' });
            const rect = (el) => {
                const r = el.getBoundingClientRect();
                return [r.left, r.top, r.right, r.bottom, r.width, r.height];
            };
            return Array.from(row.querySelectorAll('.episode-action-btn')).map(rect);
        }");
        AssertTouchTargets(actionBoxes, "action");

        // The bottom edge of mark-completed must not fall into mark-all-until.
        requests.Clear();
        await page.Mouse.ClickAsync((float)(actionBoxes[0][0] + actionBoxes[0][4] / 2), (float)(actionBoxes[0][1] + actionBoxes[0][5] - 2));
        await page.WaitForTimeoutAsync(500);
        await Expect(page.Locator("#confirmMarkCompletedUntilModal")).Not.ToBeVisibleAsync();
        Assert.That(await page.Locator("#confirmMarkCompletedUntilModal.show").CountAsync(), Is.EqualTo(0),
            "the mark-all modal must not open from the mark button's edge");
        Assert.That(requests, Has.Some.Contains("/Stations/MarkCompleted?"),
            "the single mark button handled the edge click");
        Assert.That(requests, Has.None.Contains("/Stations/MarkCompletedUntil"),
            "the mark-all-until handler must not fire");
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
        await Page.ClickAsync("#shareMenuBtn");

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
        Assert.That(styles[2], Is.EqualTo("30px"), "the confirmation title is 30 px Sulphur (spec 7.1)");
        Assert.That(styles[3], Is.EqualTo("13px"), "editor modal body size");
        Assert.That(styles[4], Is.EqualTo("13px"), "the confirmation body is 13 px (spec 7.1)");
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
