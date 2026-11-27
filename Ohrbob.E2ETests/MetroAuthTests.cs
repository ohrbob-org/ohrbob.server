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
public class MetroAuthTests : E2EPageTest
{

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        IgnoreHTTPSErrors = true
    };

    [SetUp]
    public void Setup()
    {
        SignOut();
    }

    [TearDown]
    public void TearDown()
    {
        SignIn();
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
    public async Task ClientsOpenWebAppNavigatesToANightLoginPage()
    {
        _factory.CreateClient();
        await Page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });
        await Context.AddCookiesAsync(new[]
        {
            new Cookie { Name = "ohrbob_theme", Value = "light", Url = _factory.ServerAddress }
        });

        var boosted = new List<string>();
        Page.Request += (_, request) =>
        {
            var hx = request.Headers.FirstOrDefault(h =>
                h.Key.Equals("HX-Request", StringComparison.OrdinalIgnoreCase));
            if (hx.Key != null && hx.Value.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                boosted.Add($"{request.Method} {request.Url}");
            }
        };

        await Page.GotoAsync($"{_factory.ServerAddress}/Home/Clients");
        await Page.ClickAsync("a:has-text('Open Web App')");

        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/Home/Login/?$"));
        Assert.That(boosted, Is.Empty, "the login redirect must be a full navigation, not htmx-boosted");

        await Expect(Page.Locator("body.metro-night")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".auth-page.metro-night")).ToHaveCountAsync(1);

        var m = await Page.EvaluateAsync<double[]>(@"() => {
            const parse = (rgb) => {
                const m = rgb.match(/[\d.]+/g);
                return { r: +m[0], g: +m[1], b: +m[2], a: m.length > 3 ? +m[3] : 1 };
            };
            const lum = (c) => {
                const f = (v) => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); };
                return 0.2126 * f(c.r) + 0.7152 * f(c.g) + 0.0722 * f(c.b);
            };
            const page = document.querySelector('.auth-page');
            const card = document.querySelector('.auth-card');
            const pageBg = parse(getComputedStyle(page).backgroundColor);
            const cardBg = parse(getComputedStyle(card).backgroundColor);
            const bg = {
                r: cardBg.r * cardBg.a + pageBg.r * (1 - cardBg.a),
                g: cardBg.g * cardBg.a + pageBg.g * (1 - cardBg.a),
                b: cardBg.b * cardBg.a + pageBg.b * (1 - cardBg.a)
            };
            const ghost = document.querySelector('.auth-card .btn-ghost');
            if (!ghost) return [lum(pageBg), 0, 0];
            const fg = parse(getComputedStyle(ghost).color);
            const ratio = (Math.max(lum(fg), lum(bg)) + 0.05) / (Math.min(lum(fg), lum(bg)) + 0.05);
            return [lum(pageBg), 1, ratio];
        }");
        Assert.That(m[0], Is.LessThan(0.25), "the auth surface must be dark without body tokens");
        if (m[1] > 0)
        {
            Assert.That(m[2], Is.GreaterThanOrEqualTo(3),
                "ghost provider text must stay legible against the card");
        }
    }

    [Test]
    public async Task ConnectAppUsesTheSameNightCard()
    {
        SignIn();
        try
        {
            var accountService = _factory.Services.GetRequiredService<AccountApiService>();
            var account = await accountService.CreateOrUpdateAccountAsync(
                "Metro Connect", $"metro-connect-{Guid.NewGuid()}@test.com", "", "Test", Guid.NewGuid().ToString());
            await accountService.CompleteOnboardingAsync(account!.Id);
            SetAccount(account.Id);

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
            SetAccount(string.Empty);
            SignOut();
        }
    }
}
