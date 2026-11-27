using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;
using Ohrbob.E2ETests.Infrastructure;

namespace Ohrbob.E2ETests;

/// <summary>
/// Task 3 of the Metro restyle: the shared shell is a flat app bar on the canvas,
/// not a floating glass pill, and toasts use semantic status classes.
/// </summary>
[NonParallelizable]
public class MetroShellTests : PageTest
{
    private PlaywrightWebApplicationFactory _factory = null!;
    private string _testAccountId = string.Empty;

    [SetUp]
    public async Task Setup()
    {
        await Context.ClearCookiesAsync();
        _factory = new PlaywrightWebApplicationFactory();

        var accountService = _factory.Services.GetRequiredService<Ohrbob.Web.Services.AccountApiService>();
        var uniqueEmail = $"metro-shell-{Guid.NewGuid()}@test.com";
        var account = await accountService.CreateOrUpdateAccountAsync(
            "E2E Metro Shell Test", uniqueEmail, "", "Test", Guid.NewGuid().ToString());

        if (account != null)
        {
            await accountService.CompleteOnboardingAsync(account.Id);
            var profileService = _factory.Services.GetRequiredService<Ohrbob.Web.Services.ProfilesApiService>();
            var profiles = await profileService.GetProfilesByAccountIdAsync(account.Id);
            if (profiles == null || !profiles.Any())
            {
                await profileService.CreateProfileAsync(
                    account.Id, new Ohrbob.Server.Shared.Data.Profile { Name = "Metro Profile" });
            }
        }

        _testAccountId = account!.Id;
        TestAuthHandler.TestAccountId = _testAccountId;
    }

    [TearDown]
    public async Task TearDown()
    {
        await Context.CloseAsync();
        _factory.Dispose();
        TestAuthHandler.TestAccountId = string.Empty;
    }

    [Test]
    public async Task Shell_UsesFlatMetroAppBar()
    {
        await Page.GotoAsync(_factory.ServerAddress);
        await Expect(Page.Locator(".app-header")).ToBeVisibleAsync();
        await Expect(Page.Locator(".floating-nav-pill")).ToHaveCountAsync(0);
        await Expect(Page.Locator(".app-nav-link")).ToHaveCountAsync(2);

        var css = await Page.EvaluateAsync<string[]>(@"() => {
            const h = getComputedStyle(document.querySelector('.app-header'));
            const l = getComputedStyle(document.querySelector('.app-nav-link'));
            return [h.backdropFilter, h.borderBottomLeftRadius, l.borderBottomLeftRadius];
        }");
        Assert.That(css[0], Is.EqualTo("none"));
        Assert.That(css[1], Is.EqualTo("0px"));
        Assert.That(css[2], Is.EqualTo("0px"));
    }

    [Test]
    public async Task Shell_SelfHostsHtmxWithoutCdnScripts()
    {
        await Page.GotoAsync(_factory.ServerAddress);

        var html = await Page.ContentAsync();
        Assert.That(html, Does.Not.Contain("unpkg.com"),
            "The shell must not load htmx from a third-party CDN");

        var scripts = await Page.EvaluateAsync<string[]>(
            "() => Array.from(document.scripts).map(s => s.src)");
        Assert.That(scripts.Any(s => s.Contains("/lib/htmx/htmx.min.js")), Is.True,
            "The local htmx bundle must be referenced");

        Assert.That(await Page.EvaluateAsync<bool>("() => typeof window.htmx !== 'undefined'"), Is.True,
            "The local htmx bundle must load and execute");
    }

    [Test]
    public async Task ChangelogModalCarriesTheMetroAccent()
    {
        var env = _factory.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
        var version = _factory.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<Ohrbob.Web.Configuration.AppVersionOptions>>()
            .Value.Tag;

        var path = Path.Combine(env.ContentRootPath, "changelog.md");
        try
        {
            await File.WriteAllTextAsync(path, "## Test release\n\n- A change worth showing.");
            await Page.GotoAsync(_factory.ServerAddress);

            var modal = Page.Locator("#changelogModal.show");
            await Expect(modal).ToBeVisibleAsync(new() { Timeout = 15000 });
            await Expect(Page.Locator("#changelogModal .modal-title")).ToContainTextAsync(version);

            var accent = Page.Locator("#changelogModal .metro-modal-accent");
            await Expect(accent).ToHaveCountAsync(1);
            var style = await accent.EvaluateAsync<string[]>(
                "el => [getComputedStyle(el).height, getComputedStyle(el).backgroundColor]");
            Assert.That(style[0], Is.EqualTo("6px"), "the changelog modal has the 6 px accent bar");
            Assert.That(style[1], Is.EqualTo("rgb(252, 93, 0)"), "the changelog accent is orange");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public async Task Toast_UsesMetroStatusClasses()
    {
        await Page.GotoAsync(_factory.ServerAddress);
        await Page.EvaluateAsync(@"() => document.body.dispatchEvent(
            new CustomEvent('showToast', { detail: { message: 'Saved', type: 'success' } }))");

        var toast = Page.Locator("#liveToast");
        await Expect(toast).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex(@"\bis-success\b"));
        await Expect(toast).Not.ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex(@"\bbg-success\b"));
    }
}
