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
    public async Task ThemeCssSourceMapIsServed()
    {
        await Page.GotoAsync(_factory.ServerAddress);
        var response = await Page.GotoAsync($"{_factory.ServerAddress}/css/theme.css.map");
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Status, Is.EqualTo(200), "theme.css advertises a source map that must not 404");
        Assert.That(response.Headers.TryGetValue("content-type", out var contentType), Is.True);
        Assert.That(contentType, Does.Contain("json"));
        var body = await Page.EvaluateAsync<string>("() => document.body.innerText");
        Assert.That(body, Does.Contain("\"version\""), "the served file is a real source map");
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
        await OpenAppearanceAsync();

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
        await OpenAppearanceAsync();
        await Expect(Page.Locator("input[name='theme'][value='dark']")).ToBeCheckedAsync();

        // Switching back to "system" is stored as such.
        await Page.Locator("label[for='themeSystem']").ClickAsync();
        Assert.That(await CookieValueAsync("ohrbob_theme"), Is.EqualTo("system"),
            "Choosing System must store the system mode, not a resolved light/dark value");
    }

    /// <summary>
    /// /Account shows the overview tiles first; the Appearance selector lives in
    /// the hidden details container until its tile is opened.
    /// </summary>
    private async Task OpenAppearanceAsync()
    {
        await Page.Locator("#appearance-tab").ClickAsync();
        await Expect(Page.Locator("#appearance-pane")).ToBeVisibleAsync(new() { Timeout = 15000 });
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
        ContrastChecks.AssertNoIssues(await Page.EvaluateAsync<string[]>(ContrastChecks.Script), "expanded player");

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

    /// <summary>The station editor's episode rows are feed-colour tiles, matching the feed card.</summary>
    [Test]
    public async Task Theme_EpisodeRowsUseTheirFeedColourBackground()
    {
        if (!await TestCasts.IsAvailableAsync())
        {
            Assert.Ignore($"podcast-testcasts is not reachable at {TestCasts.ConfigureBaseUrl}.");
        }

        var slug = $"{Guid.NewGuid():N}";
        await TestCasts.ConfigureAsync(new TestCastPodcast(slug, "Dark Card Feed", new[]
        {
            new TestCastEpisode("Dark Card Episode", $"{slug}-1.mp3"),
        }));

        var stations = _factory.Services.GetRequiredService<StationsApiService>();
        var station = await stations.CreateStationAsync(_accountId, new Station
        {
            AccountId = _accountId,
            Name = "Dark Card Station",
            Feeds = new[] { TestCasts.FeedUrl(slug) }
        });
        var profiles = _factory.Services.GetRequiredService<ProfilesApiService>();
        await profiles.AddStationToProfileAsync(_profileId, station!.Id);
        await WaitForEpisodesAsync(station.Id, 1);

        await Context.AddCookiesAsync(new[]
        {
            new Cookie { Name = "ohrbob_theme", Value = "dark", Url = _factory.ServerAddress }
        });
        await Page.SetViewportSizeAsync(1280, 900);
        await Page.GotoAsync($"{_factory.ServerAddress}/Stations/Edit/{station.Id}");

        var card = Page.Locator("#resolvedList .card-episode.resolved-item").First;
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 15000 });

        // The editor re-renders the playlist in place (feed refresh), which detaches the
        // card for a moment; a detached element has an empty computed style, so retry.
        var measured = Array.Empty<string>();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            measured = await Page.EvaluateAsync<string[]>(@"() => {
                const el = document.querySelector('#resolvedList .card-episode.resolved-item');
                if (!el) return [];
                const accent = getComputedStyle(el).getPropertyValue('--card-episode-accent').trim();
                const probe = document.createElement('span');
                probe.style.backgroundColor = accent;
                document.body.appendChild(probe);
                const expected = getComputedStyle(probe).backgroundColor;
                probe.remove();
                const s = getComputedStyle(el);
                return [s.backgroundColor, expected, s.borderLeftWidth];
            }");
            if (measured.Length == 3 && measured[0].StartsWith("rgb", StringComparison.Ordinal))
            {
                break;
            }
            await Page.WaitForTimeoutAsync(200);
        }

        Assert.That(measured, Has.Length.EqualTo(3), "Could not read the episode row surface");
        Assert.That(measured[0], Is.EqualTo(measured[1]),
            "the episode row is filled with its feed colour, like the feed card");
        Assert.That(measured[2], Is.EqualTo("4px"), "the row keeps its 4 px feed border");
    }

    private async Task AssertNoContrastIssuesAsync(string path)
    {
        await Page.GotoAsync(_factory.ServerAddress + path);
        Assert.That(await CurrentThemeAsync(), Is.EqualTo("dark"), $"Expected the dark theme on {path}");
        ContrastChecks.AssertNoIssues(await Page.EvaluateAsync<string[]>(ContrastChecks.Script), path);
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

    private async Task<string?> CookieValueAsync(string name)
    {
        var cookies = await Context.CookiesAsync();
        return cookies.FirstOrDefault(c => c.Name == name)?.Value;
    }
}
