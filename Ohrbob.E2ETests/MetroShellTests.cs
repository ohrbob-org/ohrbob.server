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
