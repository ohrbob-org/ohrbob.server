using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using Ohrbob.E2ETests.Infrastructure;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Services;

namespace Ohrbob.E2ETests;

/// <summary>
/// Issue #7: ohrbob.web dark mode. The theme follows the browser/OS preference by default and
/// can be overridden (system/light/dark) from the Appearance section on /Account.
/// </summary>
[NonParallelizable]
public class ThemeTests : PageTest
{
    private PlaywrightWebApplicationFactory _factory = null!;
    private string _accountId = string.Empty;
    private string _profileId = string.Empty;

    public override BrowserNewContextOptions ContextOptions() => new() { IgnoreHTTPSErrors = true };

    [SetUp]
    public async Task Setup()
    {
        await Context.ClearCookiesAsync();
        _factory = new PlaywrightWebApplicationFactory();
        Page.Console += (_, msg) => Console.WriteLine($"BROWSER CONSOLE: {msg.Text}");
        Page.PageError += (_, msg) => Console.WriteLine($"BROWSER ERROR: {msg}");

        var accountService = _factory.Services.GetRequiredService<AccountApiService>();
        var account = await accountService.CreateOrUpdateAccountAsync(
            "Theme Tester", $"theme-{Guid.NewGuid()}@test.com", "", "Test", Guid.NewGuid().ToString());
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
    public async Task Theme_FollowsOperatingSystemPreferenceByDefault()
    {
        await Page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
        await Page.GotoAsync(_factory.ServerAddress);
        Assert.That(await CurrentThemeAsync(), Is.EqualTo("dark"),
            "With no saved preference the theme must follow the OS (dark)");

        // While in "system" mode the UI must follow a live OS change.
        await Page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-bs-theme", "light", new() { Timeout = 5000 });
    }

    [Test]
    public async Task Theme_ExplicitCookieOverridesSystemPreference()
    {
        await Page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
        await Page.GotoAsync(_factory.ServerAddress);
        Assert.That(await CurrentThemeAsync(), Is.EqualTo("dark"));

        await Context.AddCookiesAsync(new[]
        {
            new Cookie { Name = "ohrbob_theme", Value = "light", Url = _factory.ServerAddress }
        });
        await Page.ReloadAsync();

        Assert.That(await CurrentThemeAsync(), Is.EqualTo("light"),
            "An explicit saved preference must win over the OS preference");
    }

    [Test]
    public async Task Account_CanChangeTheme_AndItPersists()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Account");

        var darkLabel = Page.Locator("label[for='themeDark']");
        await darkLabel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await darkLabel.ClickAsync();

        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-bs-theme", "dark", new() { Timeout = 5000 });
        Assert.That(await CookieValueAsync("ohrbob_theme"), Is.EqualTo("dark"),
            "Choosing a theme must persist it to the ohrbob_theme cookie");

        // The choice is remembered across a full page load, and reflected in the settings.
        await Page.GotoAsync(_factory.ServerAddress);
        Assert.That(await CurrentThemeAsync(), Is.EqualTo("dark"), "The saved theme must survive navigation");
        await Page.GotoAsync($"{_factory.ServerAddress}/Account");
        await Expect(Page.Locator("input[name='theme'][value='dark']")).ToBeCheckedAsync();

        // Switching back to "system" is stored as such.
        await Page.Locator("label[for='themeSystem']").ClickAsync();
        Assert.That(await CookieValueAsync("ohrbob_theme"), Is.EqualTo("system"),
            "Choosing System must store the system mode, not a resolved light/dark value");
    }

    private async Task<string?> CurrentThemeAsync() =>
        await Page.EvaluateAsync<string?>("() => document.documentElement.getAttribute('data-bs-theme')");

    // Task 3: in dark mode no visible text or icon control may be unreadable (either
    // dark-on-dark or, on a stray light surface, light-on-light).
    [Test]
    public async Task Theme_Dark_HasNoUnreadableTextOrControls()
    {
        if (!await TestCasts.IsAvailableAsync())
        {
            Assert.Ignore($"podcast-testcasts is not reachable at {TestCasts.ConfigureBaseUrl}.");
        }

        var slug = $"{Guid.NewGuid():N}";
        await TestCasts.ConfigureAsync(new TestCastPodcast(slug, "Theme Feed", new[]
        {
            new TestCastEpisode("Theme Episode", $"{slug}-1.mp3"),
        }));

        var stations = _factory.Services.GetRequiredService<StationsApiService>();
        var station = await stations.CreateStationAsync(_accountId, new Station
        {
            AccountId = _accountId,
            Name = "Theme Station",
            Feeds = new[] { TestCasts.FeedUrl(slug) }
        });
        var profiles = _factory.Services.GetRequiredService<ProfilesApiService>();
        await profiles.AddStationToProfileAsync(_profileId, station!.Id);
        await WaitForEpisodesAsync(station.Id, 1);

        // An explicit saved cookie must win over the OS preference.
        await Page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });
        await Context.AddCookiesAsync(new[]
        {
            new Cookie { Name = "ohrbob_theme", Value = "dark", Url = _factory.ServerAddress }
        });

        await AssertNoContrastIssuesAsync("/");
        await AssertNoContrastIssuesAsync("/Account");
        await AssertNoContrastIssuesAsync($"/Stations/Edit/{station.Id}");

        // The expanded player (bottom bar + slide-up card).
        await Page.GotoAsync(_factory.ServerAddress);
        await Page.EvaluateAsync("() => { document.getElementById('ohrbob-player').isExpanded = true; }");
        await Page.WaitForTimeoutAsync(400);
        AssertNoContrastIssues(await Page.EvaluateAsync<string[]>(ContrastScript), "expanded player");

        // The anonymous landing page (which carries its own inline styles).
        TestAuthHandler.IsAuthenticated = false;
        try
        {
            await AssertNoContrastIssuesAsync("/");
        }
        finally
        {
            TestAuthHandler.IsAuthenticated = true;
        }
    }

    [Test]
    public async Task Theme_GarbageCookieFallsBackToSystem()
    {
        await Page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
        await Context.AddCookiesAsync(new[]
        {
            new Cookie { Name = "ohrbob_theme", Value = "purple", Url = _factory.ServerAddress }
        });

        await Page.GotoAsync(_factory.ServerAddress);

        Assert.That(await CurrentThemeAsync(), Is.EqualTo("dark"),
            "An unknown saved value must fall back to the system preference");
    }

    /// <summary>Locks in the no-flash-of-wrong-theme property: the script must be in
    /// &lt;head&gt;, before the stylesheets, and run synchronously.</summary>
    [Test]
    public async Task Theme_ScriptRunsInHeadBeforeStylesheets()
    {
        await Page.GotoAsync(_factory.ServerAddress);
        var html = await Page.ContentAsync();

        var scriptIdx = html.IndexOf("/js/theme.js", StringComparison.Ordinal);
        var cssIdx = html.IndexOf("/css/theme.css", StringComparison.Ordinal);
        Assert.That(scriptIdx, Is.GreaterThan(-1), "theme.js must be referenced");
        Assert.That(cssIdx, Is.GreaterThan(-1), "theme.css must be referenced");
        Assert.That(scriptIdx, Is.LessThan(cssIdx),
            "theme.js must load before the stylesheets so the theme is applied before paint");

        var tagStart = html.LastIndexOf("<script", scriptIdx, StringComparison.Ordinal);
        var tag = html.Substring(tagStart, scriptIdx - tagStart);
        Assert.That(tag, Does.Not.Contain("defer").And.Not.Contain("async"),
            "theme.js must run synchronously (not defer/async)");
    }

    /// <summary>Listen and the station editor must render the theme's card components,
    /// not generic Bootstrap cards, so the three content levels stay distinguishable.</summary>
    [Test]
    public async Task Cards_UseSharedThemeComponents()
    {
        if (!await TestCasts.IsAvailableAsync())
        {
            Assert.Ignore($"podcast-testcasts is not reachable at {TestCasts.ConfigureBaseUrl}.");
        }

        // The editor's tab pills only render below the lg breakpoint.
        await Page.SetViewportSizeAsync(375, 812);

        var slug = $"{Guid.NewGuid():N}";
        await TestCasts.ConfigureAsync(new TestCastPodcast(slug, "Card Feed", new[]
        {
            new TestCastEpisode("Card Episode", $"{slug}-1.mp3"),
        }));

        var stations = _factory.Services.GetRequiredService<StationsApiService>();
        var station = await stations.CreateStationAsync(_accountId, new Station
        {
            AccountId = _accountId,
            Name = "Card Station",
            Feeds = new[] { TestCasts.FeedUrl(slug) }
        });
        var profiles = _factory.Services.GetRequiredService<ProfilesApiService>();
        await profiles.AddStationToProfileAsync(_profileId, station!.Id);
        await WaitForEpisodesAsync(station.Id, 1);

        await Page.GotoAsync(_factory.ServerAddress);
        await Expect(Page.Locator(".card-station", new() { HasTextString = "Card Station" }))
            .ToBeVisibleAsync(new() { Timeout = 15000 });

        await Page.GotoAsync($"{_factory.ServerAddress}/Stations/Edit/{station.Id}");
        await Page.ClickAsync("#resolved-tab");
        await Expect(Page.Locator("#resolvedList .card-episode.resolved-item"))
            .ToHaveCountAsync(1, new() { Timeout = 15000 });
    }

    private async Task AssertNoContrastIssuesAsync(string path)
    {
        await Page.GotoAsync(_factory.ServerAddress + path);
        Assert.That(await CurrentThemeAsync(), Is.EqualTo("dark"), $"Expected the dark theme on {path}");
        AssertNoContrastIssues(await Page.EvaluateAsync<string[]>(ContrastScript), path);
    }

    private static void AssertNoContrastIssues(string[] offenders, string label) =>
        Assert.That(offenders, Is.Empty,
            $"Unreadable text/controls in dark mode ({label}):\n  {string.Join("\n  ", offenders)}");

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

    // Composites translucent backgrounds/text colours over their ancestors so a
    // subtle white-on-dark surface is not mistaken for a light one.
    private const string ContrastScript = @"() => {
        const parse = (rgb) => {
            const m = rgb.match(/[\d.]+/g);
            if (!m) return { r: 0, g: 0, b: 0, a: 0 };
            return { r: +m[0], g: +m[1], b: +m[2], a: m.length > 3 ? +m[3] : 1 };
        };
        const luma = (c) => (0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b) / 255;
        const effectiveBg = (el) => {
            const chain = [];
            let node = el;
            while (node && node.nodeType === 1) { chain.push(node); node = node.parentElement; }
            let acc = { r: 255, g: 255, b: 255 };
            for (let i = chain.length - 1; i >= 0; i--) {
                const c = parse(getComputedStyle(chain[i]).backgroundColor);
                if (c.a <= 0) continue;
                acc = { r: c.r * c.a + acc.r * (1 - c.a), g: c.g * c.a + acc.g * (1 - c.a), b: c.b * c.a + acc.b * (1 - c.a) };
            }
            return acc;
        };
        const offenders = [];
        document.querySelectorAll('body *').forEach((el) => {
            if (el instanceof SVGElement) return;
            const direct = Array.from(el.childNodes).some((n) => n.nodeType === 3 && n.textContent.trim().length > 0);
            const isControl = el.matches('button, a, .btn, [role=""button""], [role=""slider""]');
            if (!direct && !isControl) return;
            const cs = getComputedStyle(el);
            if (cs.display === 'none' || cs.visibility === 'hidden' || parseFloat(cs.opacity) === 0) return;
            const rect = el.getBoundingClientRect();
            if (rect.width === 0 || rect.height === 0) return;

            const bg = effectiveBg(el);
            const fgColor = parse(cs.color);
            const fg = {
                r: fgColor.r * fgColor.a + bg.r * (1 - fgColor.a),
                g: fgColor.g * fgColor.a + bg.g * (1 - fgColor.a),
                b: fgColor.b * fgColor.a + bg.b * (1 - fgColor.a)
            };
            const bl = luma(bg);
            const fl = luma(fg);
            if ((bl < 0.4 && fl < 0.45) || (bl > 0.6 && fl > 0.55)) {
                const cls = typeof el.className === 'string' ? el.className : '';
                offenders.push(el.tagName + '.' + cls + ' (fg=' + fl.toFixed(2) + ', bg=' + bl.toFixed(2) + ')');
            }
        });
        return offenders.slice(0, 25);
    }";

    private async Task<string?> CookieValueAsync(string name)
    {
        var cookies = await Context.CookiesAsync();
        return cookies.FirstOrDefault(c => c.Name == name)?.Value;
    }
}
