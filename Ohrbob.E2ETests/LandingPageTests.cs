using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;
using Ohrbob.E2ETests.Infrastructure;

namespace Ohrbob.E2ETests;

public class LandingPageTests : E2EPageTest
{

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
    public async Task LandingPage_HasFloatingNav_And_SplitLayout()
    {
        _factory.CreateClient(); // Start the server
        var url = _factory.ServerAddress;
        await Page.GotoAsync(url);

        // Check for the flat Metro app bar
        await Expect(Page.Locator(".app-header")).ToBeVisibleAsync();
        
        // Check for landing layout
        await Expect(Page.Locator(".radio-landing-container")).ToBeVisibleAsync();
        await Expect(Page.Locator(".step-text-box").First).ToBeVisibleAsync();

        // Check that unauthenticated user sees the footer and its links
        await Expect(Page.Locator("footer")).ToBeVisibleAsync();
        await Expect(Page.Locator("footer >> text=About")).ToBeVisibleAsync();
        await Expect(Page.Locator("footer >> a[href*='github.com']")).ToBeVisibleAsync();
    }

    [Test]
    public async Task LandingPage_ContentIsVisibleWithoutScrollReveal()
    {
        _factory.CreateClient(); // Start the server
        await Page.GotoAsync(_factory.ServerAddress);

        await Expect(Page.Locator("h1#landing-title")).ToBeVisibleAsync();

        // The feature copy must be readable right away, not faded in on scroll.
        var textBox = Page.Locator(".step-text-box").First;
        await Expect(textBox).ToBeVisibleAsync();
        var opacity = await textBox.EvaluateAsync<string>("el => getComputedStyle(el).opacity");
        Assert.That(opacity, Is.EqualTo("1"));

        await Expect(Page.Locator("h2", new PageLocatorOptions { HasTextString = "Tune the rotation" })).ToBeVisibleAsync();
    }

    [Test]
    public async Task LandingPage_UsesDarkMetroPoster()
    {
        _factory.CreateClient();
        await Page.GotoAsync(_factory.ServerAddress);

        await Expect(Page.Locator("body.metro-night")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".landing-page.metro-night")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".landing-hero .landing-title#landing-title")).ToBeVisibleAsync();
        Assert.That(await Page.Locator(".screenshot-card").CountAsync(), Is.GreaterThanOrEqualTo(1),
            "the poster must render at least one screenshot to measure");

        var m = await Page.EvaluateAsync<double[]>(@"() => {
            const page = getComputedStyle(document.querySelector('.landing-page'));
            const shot = document.querySelector('.screenshot-card');
            const title = getComputedStyle(document.querySelector('.landing-title'));
            const m = page.backgroundColor.match(/[\d.]+/g).slice(0, 3).map(Number);
            return [ (0.2126*m[0] + 0.7152*m[1] + 0.0722*m[2]) / 255,
                     parseFloat(getComputedStyle(shot).borderRadius),
                     parseFloat(title.fontSize) ];
        }");
        Assert.That(m[0], Is.LessThan(0.25), "landing background must be dark");
        Assert.That(m[1], Is.EqualTo(0), "screenshots must be square");
        Assert.That(m[2], Is.GreaterThan(56), "hero title must be display-sized");
    }

    [Test]
    public async Task LandingPage_FeatureTilesAreColorBlocks()
    {
        _factory.CreateClient();
        await Page.GotoAsync(_factory.ServerAddress);

        await Expect(Page.Locator(".landing-tiles > .landing-tile")).ToHaveCountAsync(6);

        var tiles = await Page.EvaluateAsync<string[]>(@"() => {
            const tiles = Array.from(document.querySelectorAll('.landing-tiles > .landing-tile'));
            const backgrounds = tiles.map(t => getComputedStyle(t).backgroundColor);
            const borders = tiles.map(t => getComputedStyle(t).borderTopWidth);
            return [...backgrounds, ...borders];
        }");
        var backgrounds = tiles.Take(6).ToArray();
        Assert.That(backgrounds.Distinct().Count(), Is.GreaterThanOrEqualTo(4),
            "the feature tiles cycle through the brand colours");
        Assert.That(backgrounds, Has.None.EqualTo("rgba(0, 0, 0, 0)"),
            "every feature tile is a solid colour block");
        Assert.That(tiles.Skip(6).ToArray(), Has.All.EqualTo("0px"),
            "feature tiles have no hairline borders");
    }

    [Test]
    public async Task LandingPage_StaysDarkAcrossBoostedNavigation()
    {
        _factory.CreateClient();
        await Page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });
        await Context.AddCookiesAsync(new[]
        {
            new Cookie { Name = "ohrbob_theme", Value = "light", Url = _factory.ServerAddress }
        });

        // Start on a normal marketing page: no night tokens on <body> yet.
        await Page.GotoAsync($"{_factory.ServerAddress}/Home/Faq");
        await Expect(Page.Locator("body")).Not.ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex(@"\bmetro-night\b"));

        // A marker on the header survives only if the navigation is boosted:
        // htmx swaps #main-content instead of reloading the whole document.
        await Page.EvaluateAsync(
            "() => { document.querySelector('.app-header').dataset.boostMarker = 'kept'; }");
        await Page.ClickAsync(".app-header .metro-brandline");
        await Expect(Page.Locator("h1#landing-title")).ToBeVisibleAsync();
        await Expect(Page.Locator(".app-header")).ToHaveAttributeAsync("data-boost-marker", "kept");

        // The swapped poster must re-apply the night body class and stay dark.
        await Expect(Page.Locator("body")).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex(@"\bmetro-night\b"));
        var dark = await Page.EvaluateAsync<bool>(@"() => {
            const cs = getComputedStyle(document.querySelector('.landing-page'));
            const m = cs.backgroundColor.match(/[\d.]+/g).slice(0, 3).map(Number);
            return (0.2126*m[0] + 0.7152*m[1] + 0.0722*m[2]) / 255 < 0.25;
        }");
        Assert.That(dark, Is.True, "the boosted poster must keep the night tokens");

        // Leaving through another boosted link must clear the body class again.
        await Page.ClickAsync(".app-footer a[href='/Home/Features']");
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/Home/Features/?$"));
        await Expect(Page.Locator("body")).Not.ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex(@"\bmetro-night\b"));
    }

    [Test]
    public async Task LandingPage_LongHeroFitsNarrowPhones()
    {
        _factory.CreateClient();
        await Page.SetViewportSizeAsync(320, 800);
        await Page.GotoAsync(_factory.ServerAddress);
        var overflow = await Page.EvaluateAsync<bool>(
            @"() => { const el = document.querySelector('.landing-title'); return el.scrollWidth > el.clientWidth + 1; }");
        Assert.That(overflow, Is.False, "the hero headline must wrap, not scroll sideways");
    }

    [Test]
    public async Task LandingPage_UsesOneCentredContentColumn()
    {
        _factory.CreateClient();
        await Page.SetViewportSizeAsync(1440, 900);
        await Page.GotoAsync(_factory.ServerAddress);

        // The landing grid has one right edge: Login, the hero decor, the tier
        // card and the feature tiles all end where the content column ends.
        var edges = await Page.EvaluateAsync<double[]>(@"() => {
            const rect = (selector) => document.querySelector(selector).getBoundingClientRect();
            return [
                rect('.app-header .btn-primary').right,
                rect('.landing-square--violet').right,
                rect('.landing-tier-card').right,
                rect('.landing-tiles .landing-tile').right,
                rect('.app-header .metro-brandline').left,
                rect('.landing-hero .landing-title').left,
                rect('.landing-tier-card').left
            ];
        }");

        var right = edges[0];
        Assert.That(edges[1], Is.EqualTo(right).Within(1), "hero decor aligns with Login");
        Assert.That(edges[2], Is.EqualTo(right).Within(1), "tier card aligns with Login");
        Assert.That(edges[3], Is.EqualTo(right).Within(1), "feature tiles align with Login");
        Assert.That(edges[4], Is.EqualTo(edges[5]).Within(1), "brandline aligns with the hero title");
        Assert.That(edges[6], Is.EqualTo(edges[4]).Within(1), "tier card starts where the content starts");
    }

    [Test]
    public async Task LandingPage_ComparesFreeAndMorebobTiers()
    {
        _factory.CreateClient(); // Start the server
        await Page.GotoAsync(_factory.ServerAddress);

        var table = Page.Locator(".landing-tier-table");
        await Expect(table).ToBeVisibleAsync();

        var header = table.Locator("thead");
        await Expect(header).ToContainTextAsync("Free");
        await Expect(header).ToContainTextAsync("morebob");

        await Expect(table.Locator("tbody tr", new LocatorLocatorOptions { HasTextString = "Radio stations" }))
            .ToContainTextAsync("Unlimited");
        await Expect(table.Locator("tbody tr", new LocatorLocatorOptions { HasTextString = "Podcasts" }))
            .ToContainTextAsync("10");
        await Expect(table.Locator("tbody tr", new LocatorLocatorOptions { HasTextString = "Listening profiles" }))
            .ToContainTextAsync("Up to 5");
        // App support applies to both plans, so the platforms are listed under
        // each tier column rather than in one centred, spanning cell.
        var appSupport = table.Locator("tbody tr", new LocatorLocatorOptions { HasTextString = "App support" });
        await Expect(appSupport.Locator("td")).ToHaveCountAsync(2);
        await Expect(appSupport.Locator("td").Nth(0)).ToContainTextAsync("Android Auto, Linux, Windows, Mac");
        await Expect(appSupport.Locator("td").Nth(1)).ToContainTextAsync("Android Auto, Linux, Windows, Mac");
    }
}
