using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using Ohrbob.E2ETests.Infrastructure;

namespace Ohrbob.E2ETests;

/// <summary>
/// Task 12 of the Metro restyle: the editorial pages share one column, hairline
/// sections and compact Metro titles; the FAQ, status cards and error panels keep
/// their ids and handlers but lose every rounded, shadowed surface.
/// </summary>
[NonParallelizable]
public class MetroEditorialTests : PageTest
{
    private PlaywrightWebApplicationFactory _factory = null!;

    private static readonly string[] EditorialPaths =
    {
        "/Home/Features",
        "/Home/Clients",
        "/Home/Faq",
        "/Home/SelfHost",
        "/Home/Privacy",
        "/Home/Status",
        "/Home/About"
    };

    private static readonly string[] ErrorPaths =
    {
        "/Home/ServiceUnavailable",
        "/Home/AlreadyRegistered"
    };

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        IgnoreHTTPSErrors = true,
        ViewportSize = new ViewportSize { Width = 1280, Height = 900 }
    };

    [SetUp]
    public void Setup()
    {
        TestAuthHandler.IsAuthenticated = false;
        TestAuthHandler.TestAccountId = string.Empty;
        _factory = new PlaywrightWebApplicationFactory();
        _factory.CreateClient();
        Page.Console += (_, msg) => Console.WriteLine($"BROWSER CONSOLE: {msg.Text}");
        Page.PageError += (_, err) => Console.WriteLine($"BROWSER ERROR: {err}");
    }

    [TearDown]
    public async Task TearDown()
    {
        await Context.CloseAsync();
        _factory?.Dispose();
        TestAuthHandler.IsAuthenticated = true;
        TestAuthHandler.TestAccountId = string.Empty;
    }

    [Test]
    public async Task EditorialPagesUseMetroSections()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Home/Features");
        await Expect(Page.Locator(".editorial-section").First).ToBeVisibleAsync();

        var sectionWidth = await Page.EvaluateAsync<string>(
            @"() => getComputedStyle(document.querySelector('.editorial-section')).borderTopWidth");
        Assert.That(sectionWidth, Is.EqualTo("1px"));

        var head = await Page.EvaluateAsync<string[]>(@"() => {
            const kicker = document.querySelector('.editorial .metro-kicker');
            const title = document.querySelector('.editorial .metro-title.metro-title--compact');
            const lede = document.querySelector('.editorial-lede');
            return [kicker ? getComputedStyle(kicker).textTransform : 'missing',
                    title ? getComputedStyle(title).fontFamily : 'missing',
                    lede ? getComputedStyle(lede).fontSize : 'missing',
                    lede ? getComputedStyle(lede).lineHeight : 'missing'];
        }");
        Assert.That(head[0], Is.EqualTo("lowercase"), "the kicker is lowercase");
        Assert.That(head[1], Does.Contain("Sulphur"), "the page title uses the display face");
        Assert.That(head[2], Is.EqualTo("16px"), "the lede prints at 16 px");
        Assert.That(head[3], Is.EqualTo("24px"), "the lede has 24 px leading");

        // Section hierarchy: 11 px lowercase kicker, 24 px Sulphur heading.
        var sectionHead = await Page.EvaluateAsync<string[]>(@"() => {
            const kicker = document.querySelector('.editorial .metro-kicker');
            const title = document.querySelector('.editorial-section__title');
            const k = getComputedStyle(kicker);
            const t = getComputedStyle(title);
            return [k.fontSize, k.textTransform, t.fontFamily, t.fontSize];
        }");
        Assert.That(sectionHead[0], Is.EqualTo("11px"), "section labels print at 11 px");
        Assert.That(sectionHead[1], Is.EqualTo("lowercase"), "section labels are lowercase");
        Assert.That(sectionHead[2], Does.Contain("Sulphur"), "section headings use the display face");
        Assert.That(sectionHead[3], Is.EqualTo("24px"), "section headings print at 24 px");

        await Page.GotoAsync($"{_factory.ServerAddress}/Home/Clients");
        Assert.That(await Page.Locator("[onmouseover]").CountAsync(), Is.EqualTo(0));
        var radius = await Page.EvaluateAsync<double>(
            @"() => parseFloat(getComputedStyle(document.querySelector('.client-tile')).borderRadius)");
        Assert.That(radius, Is.EqualTo(0));
    }

    [Test]
    public async Task EditorialPagesHaveNoUnreadableTextInDarkMode()
    {
        await Context.AddCookiesAsync(new[]
        {
            new Cookie { Name = "ohrbob_theme", Value = "dark", Url = _factory.ServerAddress }
        });

        foreach (var path in EditorialPaths.Concat(ErrorPaths))
        {
            await Page.GotoAsync(_factory.ServerAddress + path);
            await Expect(Page.Locator("html")).ToHaveAttributeAsync(
                "data-bs-theme", "dark", new() { Timeout = 5000 });
            ContrastChecks.AssertNoIssues(
                await Page.EvaluateAsync<string[]>(ContrastChecks.Script), path);
        }
    }

    [Test]
    public async Task ClientsUseSquareHairlineTilesWithBigIcons()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Home/Clients");
        await Expect(Page.Locator(".client-tile").First).ToBeVisibleAsync();

        var m = await Page.EvaluateAsync<string[]>(@"() => {
            const tile = document.querySelector('.client-tile');
            const icon = tile.querySelector('.client-tile__icon');
            const soon = document.querySelector('.client-tile__soon');
            const action = tile.querySelector('.client-tile__action');
            const t = getComputedStyle(tile);
            const a = action ? getComputedStyle(action) : null;
            return [t.borderTopWidth, t.borderTopColor, t.borderRadius, t.boxShadow,
                    getComputedStyle(icon).width, getComputedStyle(icon).height,
                    getComputedStyle(icon).borderRadius,
                    soon ? getComputedStyle(soon).backgroundColor : 'missing',
                    soon ? getComputedStyle(soon).color : 'missing',
                    soon ? getComputedStyle(soon).textTransform : 'missing',
                    a ? a.borderRadius : 'missing',
                    a ? a.backgroundColor : 'missing'];
        }");

        Assert.That(m[0], Is.EqualTo("1px"), "client tiles sit on a hairline");
        Assert.That(m[1], Is.EqualTo("rgba(0, 0, 0, 0)"), "the hairline is gone");
        Assert.That(m[2], Is.EqualTo("0px"), "client tiles are square");
        Assert.That(m[3], Is.EqualTo("none"), "client tiles carry no shadow");
        Assert.That(m[4], Is.EqualTo("64px"), "the icon area is 64 px wide");
        Assert.That(m[5], Is.EqualTo("64px"), "the icon area is 64 px tall");
        Assert.That(m[6], Is.EqualTo("0px"), "the icon area is square");
        Assert.That(m[7], Is.EqualTo("rgb(253, 178, 0)"), "coming soon prints on Metro yellow");
        Assert.That(m[8], Is.EqualTo("rgb(0, 25, 87)"), "coming soon prints navy");
        Assert.That(m[9], Is.EqualTo("lowercase"), "coming soon is an lowercase label");
        Assert.That(m[10], Is.EqualTo("0px"), "the client action is square");
        Assert.That(m[11], Is.EqualTo("rgb(252, 93, 0)"), "the primary client action is orange");
    }

    [Test]
    public async Task FaqIsOneHairlineAccordionAndKeepsKeyboardBehaviour()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Home/Faq");
        await Expect(Page.Locator("#faqAccordion.faq-accordion")).ToBeVisibleAsync();

        var m = await Page.EvaluateAsync<string[]>(@"() => {
            const accordion = document.querySelector('.faq-accordion');
            const item = accordion.querySelector('.accordion-item');
            const button = document.querySelector('#headingOne .accordion-button');
            const after = getComputedStyle(button, '::after');
            const s = getComputedStyle(button);
            const i = getComputedStyle(item);
            return [document.querySelectorAll('.faq-accordion').length.toString(),
                    i.borderTopWidth, i.borderBottomWidth, i.borderRadius,
                    s.fontSize, s.textTransform, s.fontWeight,
                    after.content, after.color, s.boxShadow];
        }");

        Assert.That(m[0], Is.EqualTo("1"), "the FAQ is a single accordion");
        Assert.That(m[1], Is.EqualTo("1px"), "accordion items are hairline-ruled");
        Assert.That(m[2], Is.EqualTo("1px"), "accordion items are hairline-ruled");
        Assert.That(m[3], Is.EqualTo("0px"), "accordion items are square");
        Assert.That(m[4], Is.EqualTo("11px"), "questions print at 11 px");
        Assert.That(m[5], Is.EqualTo("lowercase"), "questions are lowercase");
        Assert.That(int.Parse(m[6], System.Globalization.CultureInfo.InvariantCulture),
            Is.GreaterThanOrEqualTo(700), "questions are heavy");
        Assert.That(m[7], Does.Contain("+"), "a collapsed question shows a plus");
        Assert.That(m[8], Is.EqualTo("rgb(252, 93, 0)"), "the plus/minus is orange");
        Assert.That(m[9], Is.EqualTo("none"), "no focus/active shadow");

        // Keyboard: focus a question and press Enter via the bootstrap hooks.
        await Page.Locator("#headingOne .accordion-button").FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(Page.Locator("#collapseOne")).ToBeVisibleAsync();

        var openAfter = await Page.EvaluateAsync<string>(
            @"() => getComputedStyle(document.querySelector('#headingOne .accordion-button'), '::after').content");
        Assert.That(openAfter, Does.Contain("\u2212"), "an open question shows a minus");

        // One item at a time: opening the second closes the first (data-bs-parent).
        await Page.Locator("#headingTwo .accordion-button").ClickAsync();
        await Expect(Page.Locator("#collapseTwo")).ToBeVisibleAsync();
        await Expect(Page.Locator("#collapseOne")).ToBeHiddenAsync();
    }

    [Test]
    public async Task SelfHostCodeBlockIsInkOnCanvas()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Home/SelfHost");
        await Expect(Page.Locator(".editorial-code").First).ToBeVisibleAsync();

        var m = await Page.EvaluateAsync<string[]>(@"() => {
            const code = document.querySelector('.editorial-code');
            const c = getComputedStyle(code);
            const body = getComputedStyle(document.body);
            return [c.backgroundColor, body.backgroundColor,
                    c.color, body.color,
                    c.borderTopWidth, c.borderTopColor,
                    c.fontFamily, c.fontSize,
                    c.borderRadius, c.boxShadow];
        }");

        Assert.That(m[0], Is.EqualTo(m[1]), "the code block sits on the canvas");
        Assert.That(m[2], Is.EqualTo(m[3]), "the code block prints in ink");
        Assert.That(m[4], Is.EqualTo("1px"), "the code block has a hairline");
        Assert.That(m[5], Is.EqualTo("rgba(0, 0, 0, 0)"), "the hairline is gone");
        Assert.That(m[6].ToLowerInvariant(), Does.Contain("mono"), "the code block is monospaced");
        Assert.That(m[7], Is.EqualTo("13px"), "the code block prints at 13 px");
        Assert.That(m[8], Is.EqualTo("0px"), "the code block is square");
        Assert.That(m[9], Is.EqualTo("none"), "the code block has no shadow");
    }

    [Test]
    public async Task PrivacyUsesAFlatLanguageMenuAndHairlinePanels()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Home/Privacy");
        await Expect(Page.Locator(".privacy-lang").First).ToBeVisibleAsync();

        var m = await Page.EvaluateAsync<string[]>(@"() => {
            const menu = document.querySelector('.privacy-lang');
            const option = menu.querySelector('[data-privacy-lang]');
            const summary = document.querySelector('.privacy-summary');
            const summaryLabel = summary.querySelector('strong');
            return [document.querySelectorAll('[data-privacy-lang]').length.toString(),
                    document.querySelectorAll('.privacy-lang .dropdown').length.toString(),
                    getComputedStyle(option).borderRadius,
                    getComputedStyle(option).borderTopWidth,
                    document.querySelectorAll('.privacy-summary').length.toString(),
                    getComputedStyle(summary).borderTopWidth,
                    getComputedStyle(summary).borderRadius,
                    getComputedStyle(menu).borderTopWidth,
                    getComputedStyle(summaryLabel).fontSize,
                    getComputedStyle(summaryLabel).textTransform];
        }");

        Assert.That(m[0], Is.EqualTo("2"), "both languages remain available");
        Assert.That(m[1], Is.EqualTo("0"), "the language menu is flat, not a dropdown");
        Assert.That(m[2], Is.EqualTo("0px"), "language options are square");
        Assert.That(m[3], Is.EqualTo("1px"), "language options are hairline buttons");
        Assert.That(int.Parse(m[4], System.Globalization.CultureInfo.InvariantCulture),
            Is.GreaterThanOrEqualTo(4), "summary panels render in both languages");
        Assert.That(m[5], Is.EqualTo("1px"), "summary panels are hairline-ruled");
        Assert.That(m[6], Is.EqualTo("0px"), "summary panels are square");
        Assert.That(m[8], Is.EqualTo("11px"), "summary labels print at 11 px");
        Assert.That(m[9], Is.EqualTo("lowercase"), "summary labels are lowercase");

        await Page.Locator("[data-privacy-lang='de']").ClickAsync();
        await Expect(Page.Locator("#privacy-de")).ToBeVisibleAsync();
        await Expect(Page.Locator("#privacy-en")).ToBeHiddenAsync();
        await Expect(Page.Locator("#privacy-language-label")).ToHaveTextAsync("Deutsch");

        await Page.Locator("[data-privacy-lang='en']").ClickAsync();
        await Expect(Page.Locator("#privacy-en")).ToBeVisibleAsync();
        await Expect(Page.Locator("#privacy-language-label")).ToHaveTextAsync("English");
    }

    [Test]
    public async Task FooterPrivacyLinkLoadsThePolicyThroughBoostedNavigation()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/");
        await Expect(Page.Locator(".app-footer__links a", new() { HasTextString = "Privacy Policy" }).First)
            .ToBeVisibleAsync();

        // A marker survives only when htmx swaps #main-content instead of reloading.
        await Page.EvaluateAsync("() => { window.__boostedNavigation = 'kept'; }");
        await Page.Locator(".app-footer__links a", new() { HasTextString = "Privacy Policy" }).First.ClickAsync();

        await Page.WaitForURLAsync("**/Home/Privacy", new() { Timeout = 15000 });
        Assert.That(await Page.EvaluateAsync<string?>("() => window.__boostedNavigation"),
            Is.EqualTo("kept"), "the footer privacy link must be boosted, not a full reload");

        // The view-local script must run after the htmx swap and unhide a policy.
        // The chosen language follows the browser/last choice, so accept either.
        var policy = Page.Locator("#privacy-de:visible, #privacy-en:visible");
        await Expect(policy).ToHaveCountAsync(1, new() { Timeout = 5000 });
        await Expect(policy.First).ToContainTextAsync(new System.Text.RegularExpressions.Regex("DSGVO|GDPR"));
        Assert.That(await Page.EvaluateAsync<string>("() => document.documentElement.lang"),
            Does.Match("^(de|en)$"), "privacy-language.js ran and set the document language");
    }

    [Test]
    public async Task StatusUsesSquareStatusCardsAndKeepsPolling()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Home/Status");
        await Expect(Page.Locator(".status-card").First).ToBeVisibleAsync();

        var m = await Page.EvaluateAsync<string[]>(@"() => {
            const card = document.querySelector('.status-card');
            const dot = card.querySelector('.status-dot');
            const version = card.querySelector('.status-card__version code');
            const host = document.getElementById('about-status');
            const c = getComputedStyle(card);
            const d = getComputedStyle(dot);
            return [document.querySelectorAll('.status-card').length.toString(),
                    c.borderTopWidth, c.borderRadius, c.boxShadow,
                    d.width, d.height, d.borderRadius,
                    getComputedStyle(version).fontFamily,
                    getComputedStyle(version).fontSize,
                    host.getAttribute('hx-get') ?? 'missing',
                    host.getAttribute('hx-trigger') ?? 'missing',
                    host.getAttribute('hx-boost') ?? 'missing'];
        }");

        Assert.That(m[0], Is.EqualTo("3"), "web, api and player each get a card");
        Assert.That(m[1], Is.EqualTo("1px"), "status cards sit on a hairline");
        Assert.That(m[2], Is.EqualTo("0px"), "status cards are square");
        Assert.That(m[3], Is.EqualTo("none"), "status cards carry no shadow");
        Assert.That(m[4], Is.EqualTo("10px"), "the status dot is 10 px wide");
        Assert.That(m[5], Is.EqualTo("10px"), "the status dot is 10 px tall");
        Assert.That(m[6], Is.EqualTo("0px"), "the status dot is square");
        Assert.That(m[7].ToLowerInvariant(), Does.Contain("mono"), "versions print in mono");
        Assert.That(m[8], Is.EqualTo("12px"), "versions print at 12 px");
        Assert.That(m[9], Is.EqualTo("/Home/AboutStatus"), "the polling endpoint is kept");
        Assert.That(m[10], Is.EqualTo("every 5s"), "the polling interval is kept");
        Assert.That(m[11], Is.EqualTo("false"), "polling stays unboosted");
    }

    [Test]
    public async Task ErrorPagesUseMetroPanels()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Home/ServiceUnavailable");
        await Expect(Page.Locator(".metro-panel").First).ToBeVisibleAsync();

        var unavailable = await Page.EvaluateAsync<string[]>(@"() => {
            const panel = document.querySelector('.metro-panel');
            const p = getComputedStyle(panel);
            const title = getComputedStyle(panel.querySelector('.metro-panel__title'));
            const body = getComputedStyle(panel.querySelector('.metro-panel__body'));
            const action = panel.querySelector('.metro-panel__action');
            const a = getComputedStyle(action);
            return [p.borderTopWidth, p.borderTopColor, p.borderRadius, p.boxShadow,
                    title.fontSize, title.fontFamily,
                    body.fontSize,
                    a.borderRadius, a.backgroundColor, action.tagName];
        }");

        Assert.That(unavailable[0], Is.EqualTo("6px"), "the error panel has a 6 px accent bar");
        Assert.That(unavailable[1], Is.EqualTo("rgb(220, 53, 69)"), "the service error bar is danger red");
        Assert.That(unavailable[2], Is.EqualTo("0px"), "the panel is square");
        Assert.That(unavailable[3], Is.EqualTo("none"), "the panel has no shadow");
        Assert.That(unavailable[4], Is.EqualTo("30px"), "the error title prints at 30 px");
        Assert.That(unavailable[5], Does.Contain("Sulphur"), "the error title uses the display face");
        Assert.That(unavailable[6], Is.EqualTo("14px"), "the error body prints at 14 px");
        Assert.That(unavailable[7], Is.EqualTo("0px"), "the error action is square");
        Assert.That(unavailable[8], Is.EqualTo("rgb(252, 93, 0)"), "the error action is Metro orange");
        Assert.That(unavailable[9], Is.EqualTo("BUTTON"), "try again remains a button");

        await Page.GotoAsync($"{_factory.ServerAddress}/Home/AlreadyRegistered");
        await Expect(Page.Locator(".metro-panel").First).ToBeVisibleAsync();

        var registered = await Page.EvaluateAsync<string[]>(@"() => {
            const panel = document.querySelector('.metro-panel');
            const action = panel.querySelector('.metro-panel__action');
            return [getComputedStyle(panel).borderTopWidth,
                    getComputedStyle(panel).borderTopColor,
                    action.tagName,
                    getComputedStyle(action).borderRadius];
        }");

        Assert.That(registered[0], Is.EqualTo("6px"), "already-registered keeps the accent bar");
        Assert.That(registered[1], Is.EqualTo("rgb(252, 93, 0)"), "already-registered uses the orange accent");
        Assert.That(registered[2], Is.EqualTo("A"), "back to sign in remains a link");
        Assert.That(registered[3], Is.EqualTo("0px"), "back to sign in is square");
    }

    [Test]
    public async Task EditorialPagesFit390()
    {
        await Page.SetViewportSizeAsync(390, 844);

        foreach (var path in EditorialPaths.Concat(ErrorPaths))
        {
            await Page.GotoAsync(_factory.ServerAddress + path);
            var m = await Page.EvaluateAsync<string[]>(@"() => {
                const vw = document.documentElement.clientWidth;
                const offenders = [];
                if (document.documentElement.scrollWidth > vw + 1) {
                    offenders.push('page scrollWidth ' + document.documentElement.scrollWidth + ' > ' + vw);
                }
                document.querySelectorAll('#main-content *').forEach((el) => {
                    const r = el.getBoundingClientRect();
                    if (r.width > 0 && r.right > vw + 1) {
                        const cls = typeof el.className === 'string' ? el.className : '';
                        offenders.push(el.tagName + '.' + cls + ' right=' + Math.round(r.right) + ' vw=' + vw);
                    }
                });
                return offenders.slice(0, 8);
            }");

            Assert.That(m, Is.Empty, $"{path} must fit 390 px:\n  " + string.Join("\n  ", m));
        }
    }
}
