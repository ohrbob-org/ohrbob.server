using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using Ohrbob.E2ETests.Infrastructure;
using Ohrbob.Web.Services;

namespace Ohrbob.E2ETests;

/// <summary>
/// Task 10 of the Metro restyle: login, app connect and handoff are the same
/// always-dark connect surface. Every assertion reads computed styles.
/// </summary>
[NonParallelizable]
public class MetroAuthTests : PageTest
{
    private PlaywrightWebApplicationFactory _factory = null!;

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        IgnoreHTTPSErrors = true
    };

    [SetUp]
    public void Setup()
    {
        TestAuthHandler.IsAuthenticated = false;
        _factory = new PlaywrightWebApplicationFactory();
    }

    [TearDown]
    public void TearDown()
    {
        _factory.Dispose();
        TestAuthHandler.IsAuthenticated = true;
    }

    [Test]
    public async Task LoginIsADarkMetroConnectCard()
    {
        _factory.CreateClient();
        await Page.GotoAsync($"{_factory.ServerAddress}/Home/Login");

        await Expect(Page.Locator("body.metro-night")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".auth-page")).ToBeVisibleAsync();
        await Expect(Page.Locator(".auth-card .login-btn").First).ToBeVisibleAsync();

        var m = await Page.EvaluateAsync<double[]>(@"() => {
            const c = document.querySelector('.auth-card');
            const accent = c.querySelector('.auth-card__accent');
            return [parseFloat(getComputedStyle(c).borderRadius),
                    parseFloat(getComputedStyle(accent).height),
                    getComputedStyle(document.body).backgroundColor.match(/[\d.]+/g).slice(0,3).map(Number)[0]];
        }");
        Assert.That(m[0], Is.EqualTo(0));
        Assert.That(m[1], Is.EqualTo(7));
        Assert.That(m[2], Is.LessThan(60), "night background must be dark even in light mode");

        // Provider buttons are full width inside the card body.
        var buttonWidths = await Page.EvaluateAsync<double[]>(@"() => {
            const body = document.querySelector('.auth-card__body');
            const cs = getComputedStyle(body);
            const content = body.clientWidth - parseFloat(cs.paddingLeft) - parseFloat(cs.paddingRight);
            return Array.from(document.querySelectorAll('.auth-card .login-btn'))
                .map(b => Math.abs(b.getBoundingClientRect().width - content));
        }");
        Assert.That(buttonWidths, Is.Not.Empty);
        Assert.That(buttonWidths.All(d => d < 1), Is.True, "provider buttons must span the card");
    }

    [Test]
    public async Task LoginCardFitsNarrowPhones()
    {
        _factory.CreateClient();
        await Page.SetViewportSizeAsync(390, 844);
        await Page.GotoAsync($"{_factory.ServerAddress}/Home/Login");

        var overflow = await Page.EvaluateAsync<bool>(
            @"() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");
        Assert.That(overflow, Is.False, "the login page must not scroll sideways at 390 px");
    }

    [Test]
    public async Task LoginHonoursReducedMotion()
    {
        _factory.CreateClient();
        await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await Page.GotoAsync($"{_factory.ServerAddress}/Home/Login");

        var motion = await Page.EvaluateAsync<string[]>(@"() => {
            const head = document.getElementById('lynx-head');
            head.classList.add('is-nodding');
            const cs = getComputedStyle(head);
            return [cs.animationName, cs.transitionDuration];
        }");
        Assert.That(motion[0], Is.EqualTo("none"), "the nod animation must be disabled");
        Assert.That(motion[1], Is.EqualTo("0s"), "the head transition must be disabled");
    }

    [Test]
    public async Task ConnectAppUsesTheSameNightCard()
    {
        TestAuthHandler.IsAuthenticated = true;
        try
        {
            var accountService = _factory.Services.GetRequiredService<AccountApiService>();
            var account = await accountService.CreateOrUpdateAccountAsync(
                "Metro Connect", $"metro-connect-{Guid.NewGuid()}@test.com", "", "Test", Guid.NewGuid().ToString());
            await accountService.CompleteOnboardingAsync(account!.Id);
            TestAuthHandler.TestAccountId = account.Id;

            _factory.CreateClient();
            await Page.GotoAsync($"{_factory.ServerAddress}/Connect/App");

            await Expect(Page.Locator("body.metro-night")).ToHaveCountAsync(1);
            await Expect(Page.Locator(".connect-card")).ToBeVisibleAsync();
            await Expect(Page.Locator("#name")).ToBeVisibleAsync();
            await Expect(Page.Locator("#profileId")).ToBeVisibleAsync();

            var m = await Page.EvaluateAsync<double[]>(@"() => {
                const c = document.querySelector('.connect-card');
                const bg = getComputedStyle(document.body).backgroundColor.match(/[\d.]+/g).slice(0,3).map(Number);
                return [parseFloat(getComputedStyle(c).borderRadius),
                        (0.2126*bg[0] + 0.7152*bg[1] + 0.0722*bg[2]) / 255];
            }");
            Assert.That(m[0], Is.EqualTo(0), "the connect card must be square");
            Assert.That(m[1], Is.LessThan(0.25), "connect must be night-dark even in light mode");
        }
        finally
        {
            TestAuthHandler.TestAccountId = string.Empty;
            TestAuthHandler.IsAuthenticated = false;
        }
    }
}
