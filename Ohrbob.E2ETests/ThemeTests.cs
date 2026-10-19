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

    private async Task<string?> CookieValueAsync(string name)
    {
        var cookies = await Context.CookiesAsync();
        return cookies.FirstOrDefault(c => c.Name == name)?.Value;
    }
}
