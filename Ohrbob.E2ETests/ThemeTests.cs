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

    // Task 3: in dark mode no text may be dark-on-dark (unreadable).
    [Test]
    public async Task Theme_Dark_HasNoUnreadableDarkOnDarkText()
    {
        var stations = _factory.Services.GetRequiredService<StationsApiService>();
        var station = await stations.CreateStationAsync(_accountId, new Station
        {
            AccountId = _accountId,
            Name = "Theme Station",
            Feeds = Array.Empty<string>()
        });
        var profiles = _factory.Services.GetRequiredService<ProfilesApiService>();
        await profiles.AddStationToProfileAsync(_profileId, station!.Id);

        await Page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light }); // the saved cookie must win
        await Context.AddCookiesAsync(new[]
        {
            new Cookie { Name = "ohrbob_theme", Value = "dark", Url = _factory.ServerAddress }
        });

        foreach (var path in new[] { "/Account", "/" })
        {
            await Page.GotoAsync(_factory.ServerAddress + path);
            Assert.That(await CurrentThemeAsync(), Is.EqualTo("dark"));
            var offenders = await Page.EvaluateAsync<string[]>(DarkOnDarkScript);
            Assert.That(offenders, Is.Empty,
                $"Unreadable (dark-on-dark) text in dark mode on {path}:\n  {string.Join("\n  ", offenders)}");
        }
    }

    private const string DarkOnDarkScript = @"() => {
        const luma = (rgb) => {
            const m = rgb.match(/[\d.]+/g);
            if (!m) return 1;
            return (0.2126 * (+m[0]) + 0.7152 * (+m[1]) + 0.0722 * (+m[2])) / 255;
        };
        const effectiveBg = (el) => {
            let node = el;
            while (node && node.nodeType === 1) {
                const bg = getComputedStyle(node).backgroundColor;
                const alpha = bg.startsWith('rgba') ? parseFloat(bg.split(',')[3]) : 1;
                if (bg !== 'transparent' && alpha > 0.05) return bg;
                node = node.parentElement;
            }
            return 'rgb(255,255,255)';
        };
        const offenders = [];
        document.querySelectorAll('body *').forEach((el) => {
            if (el instanceof SVGElement) return;
            const direct = Array.from(el.childNodes).some((n) => n.nodeType === 3 && n.textContent.trim().length > 0);
            if (!direct) return;
            const cs = getComputedStyle(el);
            if (cs.display === 'none' || cs.visibility === 'hidden' || parseFloat(cs.opacity) === 0) return;
            const bg = luma(effectiveBg(el));
            const fg = luma(cs.color);
            if (bg < 0.4 && fg < 0.45) {
                const cls = typeof el.className === 'string' ? el.className : '';
                offenders.push(el.tagName + '.' + cls + ' (fg=' + fg.toFixed(2) + ', bg=' + bg.toFixed(2) + ')');
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
