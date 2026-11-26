using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using MongoDB.Driver;
using Ohrbob.E2ETests.Infrastructure;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Services;

namespace Ohrbob.E2ETests;

/// <summary>
/// Task 7 of the Metro restyle: Discover is a canvas page with a hairline underline
/// search, horizontal recommendation shelves, flat feed cards and hairline chips.
/// Every assertion reads computed styles, not class names.
/// </summary>
[NonParallelizable]
public class MetroDiscoverTests : PageTest
{
    private readonly MongoClient _mongo = new("mongodb://localhost:27019");
    private PlaywrightWebApplicationFactory _factory = null!;
    private Feed _seededFeed = null!;

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        IgnoreHTTPSErrors = true,
        ViewportSize = new ViewportSize { Width = 1280, Height = 900 }
    };

    [SetUp]
    public async Task Setup()
    {
        await Context.ClearCookiesAsync();
        _factory = new PlaywrightWebApplicationFactory();

        var accountService = _factory.Services.GetRequiredService<AccountApiService>();
        var account = await accountService.CreateOrUpdateAccountAsync(
            "Metro Discover", $"metro-discover-{Guid.NewGuid()}@test.com", "", "Test", Guid.NewGuid().ToString());
        await accountService.CompleteOnboardingAsync(account!.Id);

        var profileService = _factory.Services.GetRequiredService<ProfilesApiService>();
        var profiles = await profileService.GetProfilesByAccountIdAsync(account.Id);
        if (profiles == null || profiles.Count == 0)
        {
            await profileService.CreateProfileAsync(account.Id, new Profile { Name = "Main Profile" });
        }

        TestAuthHandler.TestAccountId = account.Id;

        // The recommendation lanes are fed from the DB first, so a seeded Comedy feed is
        // guaranteed to be the first card in a visible lane.
        var slug = Guid.NewGuid().ToString("N");
        _seededFeed = new Feed
        {
            Id = $"metro-discover-{slug}",
            Url = $"https://example.com/metro-discover-{slug}.xml",
            Title = $"Metro Discover Extremely Long Podcast Show Name {slug}",
            Category = "Comedy",
            Description = "<p>A <b>flat ink</b> description for the Discover card.</p>",
            CreatedAt = DateTime.UtcNow
        };
        await Feeds.InsertOneAsync(_seededFeed);

        _factory.CreateClient();
    }

    [TearDown]
    public async Task TearDown()
    {
        await Feeds.DeleteOneAsync(f => f.Id == _seededFeed.Id);
        await Context.CloseAsync();
        _factory.Dispose();
        TestAuthHandler.TestAccountId = string.Empty;
    }

    private IMongoCollection<Feed> Feeds =>
        _mongo.GetDatabase("OhrbobData").GetCollection<Feed>("FeedCollection");

    private ILocator SeededCard =>
        Page.Locator(".discover-lane .card-feed", new() { HasTextString = _seededFeed.Title }).First;

    [Test]
    public async Task Discover_UsesFlatCardsAndUnderlineSearch()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Discover");
        await Expect(Page.Locator(".page-head .metro-title")).ToBeVisibleAsync();
        await Expect(SeededCard).ToBeVisibleAsync(new() { Timeout = 15000 });

        var m = await Page.EvaluateAsync<string[]>(@"() => {
            const s = getComputedStyle(document.querySelector('#discover-search'));
            const c = document.querySelector('.discover-lane .card-feed');
            const add = c && c.querySelector('.card-feed-add');
            return [s.borderBottomLeftRadius, s.borderBottomWidth,
                    parseFloat(getComputedStyle(c).borderRadius).toString(),
                    add ? parseFloat(getComputedStyle(add).width).toString() : '0'];
        }");
        Assert.That(m[0], Is.EqualTo("0px"), "the search field is square");
        Assert.That(m[1], Is.EqualTo("2px"), "the search field sits on a 2 px rule");
        Assert.That(m[2], Is.EqualTo("0"), "feed cards are square");
        Assert.That(double.Parse(m[3]), Is.GreaterThanOrEqualTo(44), "the add square is at least 44 px");
    }

    [Test]
    public async Task Discover_PageHeadAndSearchAreMetro()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Discover");

        await Expect(Page.Locator(".page-head .metro-kicker")).ToHaveTextAsync("find something new");
        await Expect(Page.Locator(".page-head .metro-title")).ToHaveTextAsync("discover");
        await Expect(Page.Locator(".page-head .metro-hint")).ToBeVisibleAsync();

        var m = await Page.EvaluateAsync<string[]>(@"() => {
            const input = document.querySelector('#discover-search');
            const search = document.querySelector('.discover-search');
            const icon = search && search.querySelector('.bi-search');
            return [getComputedStyle(input).fontSize,
                    getComputedStyle(input).backgroundColor,
                    search ? getComputedStyle(search).borderRadius : 'missing',
                    icon ? getComputedStyle(icon).display : 'missing',
                    document.querySelectorAll('.discover-search').length.toString()];
        }");
        Assert.That(m[0], Is.EqualTo("16px"), "search text is 16 px");
        Assert.That(m[1], Is.EqualTo("rgba(0, 0, 0, 0)"), "the field sits directly on the canvas");
        Assert.That(m[2], Is.EqualTo("0px"), "no card wrapper around the search");
        Assert.That(m[3], Is.Not.EqualTo("missing"), "search icon is present");
        Assert.That(m[4], Is.EqualTo("1"), "one search field");
    }

    [Test]
    public async Task Discover_LongFeedTitleTruncates()
    {
        await Page.SetViewportSizeAsync(390, 844);
        await Page.GotoAsync($"{_factory.ServerAddress}/Discover");
        await Expect(SeededCard).ToBeVisibleAsync(new() { Timeout = 15000 });

        var m = await Page.EvaluateAsync<string[]>(@"(title) => {
            const card = Array.from(document.querySelectorAll('.discover-lane .card-feed'))
                .find(c => c.textContent.includes(title));
            const t = card.querySelector('.card-feed-title');
            const style = getComputedStyle(t);
            const titleRect = t.getBoundingClientRect();
            const cardRect = card.getBoundingClientRect();
            return [style.whiteSpace, style.textOverflow, style.overflow,
                    t.scrollWidth.toString(), t.clientWidth.toString(),
                    (titleRect.right <= cardRect.right + 1).toString(),
                    (cardRect.width <= 390).toString()];
        }", _seededFeed.Title);

        Assert.That(m[0], Is.EqualTo("nowrap"), "titles stay on one line");
        Assert.That(m[1], Is.EqualTo("ellipsis"), "long titles are clipped with an ellipsis");
        Assert.That(m[2], Is.EqualTo("hidden"));
        Assert.That(int.Parse(m[3]), Is.GreaterThan(int.Parse(m[4])), "the seeded title really overflows");
        Assert.That(m[5], Is.EqualTo("true"), "the title stays inside the card");
        Assert.That(m[6], Is.EqualTo("true"), "lane cards fit a 390 px viewport and scroll inside the shelf");
    }

    [Test]
    public async Task Discover_AddAndDetailsAreRevealedAndKeyboardReachable()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Discover");
        await Expect(SeededCard).ToBeVisibleAsync(new() { Timeout = 15000 });

        var card = SeededCard;
        var add = card.Locator(".card-feed-add");
        var toggle = card.Locator(".podcast-details-toggle");
        var overlay = card.Locator(".podcast-details-overlay");

        var hidden = await Page.EvaluateAsync<string[]>(@"(title) => {
            const card = Array.from(document.querySelectorAll('.discover-lane .card-feed'))
                .find(c => c.textContent.includes(title));
            const add = getComputedStyle(card.querySelector('.card-feed-add'));
            const overlay = getComputedStyle(card.querySelector('.podcast-details-overlay'));
            return [add.opacity, add.backgroundColor, overlay.visibility];
        }", _seededFeed.Title);
        Assert.That(hidden[0], Is.EqualTo("0"), "the add square waits for hover or focus");
        Assert.That(hidden[1], Does.Match("rgb\\(252, 93, 0\\)"), "the add square is Metro orange");
        Assert.That(hidden[2], Is.EqualTo("hidden"), "the details overlay is closed by default");

        await card.HoverAsync();
        await Expect(overlay).ToBeVisibleAsync();
        await Expect(add).ToHaveCSSAsync("opacity", "1");

        await Page.Mouse.MoveAsync(0, 0);
        await Page.WaitForTimeoutAsync(250);

        await toggle.FocusAsync();
        await Expect(add).ToHaveCSSAsync("opacity", "1");
        await add.FocusAsync();
        await Expect(add).ToHaveCSSAsync("opacity", "1");

        await toggle.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(card).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bis-open\\b"));
        await Expect(toggle).ToHaveAttributeAsync("aria-expanded", "true");
        await Expect(overlay).ToBeVisibleAsync();

        await Page.Keyboard.PressAsync("Escape");
        await Expect(card).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bis-open\\b"));
        await Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");

        // The add square is a real keyboard target too.
        await add.FocusAsync();
        var modalId = await add.GetAttributeAsync("data-bs-target");
        Assert.That(modalId, Is.Not.Null.And.Not.Empty);
        await Page.Keyboard.PressAsync("Enter");
        var modal = Page.Locator(modalId!);
        await Expect(modal).ToBeVisibleAsync();
        await modal.Locator("button:has-text('Cancel')").ClickAsync();
        await Expect(modal).Not.ToBeVisibleAsync();
    }

    [Test]
    public async Task Discover_TouchAlwaysShowsTheAddSquare()
    {
        await using var context = await Browser.NewContextAsync(new()
        {
            IgnoreHTTPSErrors = true,
            HasTouch = true,
            IsMobile = true,
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{_factory.ServerAddress}/Discover");
        await Expect(page.Locator(".discover-lane .card-feed").First)
            .ToBeVisibleAsync(new() { Timeout = 15000 });

        var m = await page.EvaluateAsync<string[]>(@"() => {
            const add = document.querySelector('.discover-lane .card-feed-add');
            const style = getComputedStyle(add);
            return [style.opacity, style.pointerEvents,
                    add.getBoundingClientRect().width.toString()];
        }");
        Assert.That(m[0], Is.EqualTo("1"), "touch devices always show the add square");
        Assert.That(m[1], Is.EqualTo("auto"));
        Assert.That(double.Parse(m[2]), Is.GreaterThanOrEqualTo(44));
    }

    [Test]
    public async Task Discover_DetailsOverlayIsFlatInk()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Discover");
        await Expect(SeededCard).ToBeVisibleAsync(new() { Timeout = 15000 });

        await SeededCard.Locator(".podcast-details-toggle").ClickAsync();
        await Expect(SeededCard.Locator(".podcast-details-overlay")).ToBeVisibleAsync();

        var m = await Page.EvaluateAsync<string[]>(@"(title) => {
            const card = Array.from(document.querySelectorAll('.discover-lane .card-feed'))
                .find(c => c.textContent.includes(title));
            const overlay = getComputedStyle(card.querySelector('.podcast-details-overlay'));
            const text = getComputedStyle(card.querySelector('.podcast-details-text'));
            const close = card.querySelector('.podcast-details-close');
            const img = card.querySelector('.podcast-cover-media img, .card-feed-cover-empty');
            const cardStyle = getComputedStyle(card);
            return [overlay.backdropFilter, overlay.backgroundColor,
                    text.fontSize, text.lineHeight,
                    close ? getComputedStyle(close).display : 'missing',
                    img ? getComputedStyle(img).filter : 'missing',
                    img ? getComputedStyle(img).transform : 'missing',
                    cardStyle.transform, cardStyle.boxShadow];
        }", _seededFeed.Title);

        Assert.That(m[0], Is.EqualTo("none"), "no blur behind the description");
        Assert.That(m[1], Is.Not.EqualTo("rgba(0, 0, 0, 0)"), "the overlay is a flat ink scrim");
        Assert.That(m[2], Is.EqualTo("12px"), "description body text is 12 px");
        Assert.That(m[4], Is.Not.EqualTo("missing"), "the overlay offers a close glyph");
        Assert.That(m[5], Is.EqualTo("none"), "the cover is not blurred");
        Assert.That(m[6], Is.EqualTo("none"), "the cover does not scale");
        Assert.That(m[7], Is.EqualTo("none"), "the card does not scale");
        Assert.That(m[8], Is.EqualTo("none"), "no shadow on the card");
    }

    [Test]
    public async Task Discover_CategoryChipsAreHairlineSquares()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Discover");

        var chip = Page.Locator("#category-roster .category-chip").First;
        await Expect(chip).ToBeVisibleAsync(new() { Timeout = 15000 });

        var m = await chip.EvaluateAsync<string[]>(@"el => {
            const s = getComputedStyle(el);
            return [s.borderRadius, s.borderTopWidth, s.borderTopColor,
                    s.textTransform, s.fontSize, s.fontWeight, s.backgroundColor];
        }");
        Assert.That(m[0], Is.EqualTo("0px"), "chips are square");
        Assert.That(m[1], Is.EqualTo("1px"), "chips have a hairline");
        Assert.That(m[2], Is.Not.EqualTo("rgba(0, 0, 0, 0)"), "the hairline is visible");
        Assert.That(m[3], Is.EqualTo("uppercase"));
        Assert.That(m[4], Is.EqualTo("11px"), "chips use the 11 px label size");
        Assert.That(m[5], Is.EqualTo("700"));
        Assert.That(m[6], Is.EqualTo("rgba(0, 0, 0, 0)"), "chips are flat until hovered");

        await chip.FocusAsync();
        await Page.WaitForTimeoutAsync(250);
        var focus = await chip.EvaluateAsync<string[]>(
            "el => { const s = getComputedStyle(el); return [s.backgroundColor, s.color]; }");
        Assert.That(focus[0], Is.Not.EqualTo("rgba(0, 0, 0, 0)"), "focus fills the chip with ink");
        Assert.That(focus[1], Is.EqualTo("rgb(244, 241, 234)"), "the focused chip prints in canvas colour");
    }

    [Test]
    public async Task Discover_SearchResultsUseTheMetroGrid()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Discover?q=maus");

        await Expect(Page.Locator("#search-results .col").First)
            .ToBeVisibleAsync(new() { Timeout = 30000 });

        var m = await Page.EvaluateAsync<string[]>(@"() => {
            const grid = document.querySelector('#search-results');
            const card = document.querySelector('#search-results .card-feed');
            const title = document.querySelector('#search-results .card-feed-title');
            const head = document.querySelector('#search-results .discover-results__title');
            return [getComputedStyle(grid).display,
                    getComputedStyle(grid).gap,
                    card ? getComputedStyle(card).borderRadius : 'missing',
                    title ? getComputedStyle(title).fontSize : 'missing',
                    head ? getComputedStyle(head).textTransform : 'missing',
                    head ? getComputedStyle(head).fontSize : 'missing'];
        }");
        Assert.That(m[0], Is.EqualTo("grid"), "results sit on a Metro grid");
        Assert.That(m[2], Is.EqualTo("0px"), "result cards are square");
        Assert.That(m[3], Is.EqualTo("18px"), "result titles use the 18 px card title");
        Assert.That(m[4], Is.EqualTo("uppercase"), "the results heading is uppercase");
        Assert.That(m[5], Is.EqualTo("11px"), "the results heading uses the lane label size");
    }

    [Test]
    public async Task Discover_Fits390WithoutPageOverflow()
    {
        await Page.SetViewportSizeAsync(390, 844);
        await Page.GotoAsync($"{_factory.ServerAddress}/Discover");
        await Expect(SeededCard).ToBeVisibleAsync(new() { Timeout = 15000 });

        var m = await Page.EvaluateAsync<string[]>(@"() => {
            const track = document.querySelector('.discover-lane-track');
            const grid = document.querySelector('#search-results');
            return [document.documentElement.scrollWidth.toString(),
                    document.documentElement.clientWidth.toString(),
                    getComputedStyle(track).overflowX,
                    grid ? getComputedStyle(grid).gridTemplateColumns : 'missing'];
        }");
        Assert.That(int.Parse(m[0]), Is.LessThanOrEqualTo(int.Parse(m[1]) + 1),
            "the page itself never scrolls sideways");
        Assert.That(m[2], Is.EqualTo("auto"), "lanes scroll inside their shelf");
    }

    [Test]
    public async Task Discover_FocusRingStaysInsideTheLaneShelf()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Discover");
        await Expect(SeededCard).ToBeVisibleAsync(new() { Timeout = 15000 });

        var m = await Page.EvaluateAsync<string[]>(@"(title) => {
            const card = Array.from(document.querySelectorAll('.discover-lane .card-feed'))
                .find(c => c.textContent.includes(title));
            const track = card.closest('.discover-lane-track');

            const ring = (el) => {
                const s = getComputedStyle(el);
                const width = parseFloat(s.outlineWidth);
                const offset = parseFloat(s.outlineOffset);
                const r = el.getBoundingClientRect();
                const t = track.getBoundingClientRect();
                return {
                    width: s.outlineWidth,
                    style: s.outlineStyle,
                    color: s.outlineColor,
                    inside: r.left - offset - width >= t.left - 1 &&
                            r.top - offset - width >= t.top - 1 &&
                            r.right + offset + width <= t.right + 1 &&
                            r.bottom + offset + width <= t.bottom + 1
                };
            };

            card.querySelector('.podcast-details-toggle').focus();
            const first = ring(card);

            track.scrollLeft = track.scrollWidth;
            const lastCard = track.lastElementChild.querySelector('.card-feed');
            lastCard.querySelector('.podcast-details-toggle').focus();
            const last = ring(lastCard);

            return [first.width, first.style, first.color,
                    String(first.inside), String(last.inside)];
        }", _seededFeed.Title);

        Assert.That(m[0], Is.EqualTo("2px"), "the focused card draws a 2 px ring");
        Assert.That(m[1], Is.EqualTo("solid"));
        Assert.That(m[2], Does.Match("rgb\\(252, 93, 0\\)"), "the focus ring is Metro orange");
        Assert.That(m[3], Is.EqualTo("true"), "the first card's ring is not clipped by the shelf");
        Assert.That(m[4], Is.EqualTo("true"), "the last card's ring is not clipped by the shelf");
    }
}
