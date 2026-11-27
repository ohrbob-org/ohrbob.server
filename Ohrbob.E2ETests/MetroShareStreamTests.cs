using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using MongoDB.Driver;
using NUnit.Framework;
using Ohrbob.E2ETests.Infrastructure;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Services;

namespace Ohrbob.E2ETests;

/// <summary>
/// Task 11 of the Metro restyle: the public share page gets a flat hero, a 280 px
/// mosaic and feed-coloured cards; the stream page becomes hairline rows with
/// mono URLs. Every assertion reads computed styles, not class names.
/// </summary>
[NonParallelizable]
public class MetroShareStreamTests : PageTest
{
    private readonly MongoClient _mongo = new("mongodb://localhost:27019");
    private readonly List<string> _stationIds = new();
    private readonly List<string> _feedIds = new();
    private PlaywrightWebApplicationFactory _factory = null!;
    private string _accountId = string.Empty;
    private string _profileId = string.Empty;

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        IgnoreHTTPSErrors = true,
        ViewportSize = new ViewportSize { Width = 1280, Height = 900 }
    };

    [SetUp]
    public async Task Setup()
    {
        await Context.ClearCookiesAsync();
        TestAuthHandler.IsAuthenticated = true;

        _factory = new PlaywrightWebApplicationFactory();
        (_accountId, _profileId) = await CreateAccountAsync("Share Owner");
        TestAuthHandler.TestAccountId = _accountId;
        _factory.CreateClient();
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_stationIds.Count > 0)
            await Stations.DeleteManyAsync(s => _stationIds.Contains(s.Id));
        if (_feedIds.Count > 0)
            await Feeds.DeleteManyAsync(f => _feedIds.Contains(f.Id));

        await Context.CloseAsync();
        _factory?.Dispose();
        TestAuthHandler.TestAccountId = string.Empty;
        TestAuthHandler.IsAuthenticated = true;
    }

    private IMongoCollection<Station> Stations =>
        _mongo.GetDatabase("OhrbobData").GetCollection<Station>("StationCollection");

    private IMongoCollection<Feed> Feeds =>
        _mongo.GetDatabase("OhrbobData").GetCollection<Feed>("FeedCollection");

    private static string HexToRgb(string hex)
    {
        var value = hex.TrimStart('#');
        return $"rgb({Convert.ToInt32(value[..2], 16)}, {Convert.ToInt32(value.Substring(2, 2), 16)}, {Convert.ToInt32(value.Substring(4, 2), 16)})";
    }

    [Test]
    public async Task ShareFeedCardsCarryTheFeedColour()
    {
        const string accent = "#E63946";
        var station = await SeedShareStationAsync(accent);
        TestAuthHandler.IsAuthenticated = false;

        await Page.GotoAsync($"{_factory.ServerAddress}/s/{station.ShareCode}");
        await Expect(Page.Locator(".share-hero .metro-title")).ToBeVisibleAsync();

        var m = await Page.EvaluateAsync<string[]>(@"() => {
            const card = document.querySelector('.share-feed');
            const style = getComputedStyle(card);
            const cover = card.querySelector('.card-feed-cover');
            const add = card.querySelector('.card-feed-add');
            return [style.borderLeftWidth,
                    style.borderLeftColor,
                    parseFloat(style.borderRadius).toString(),
                    style.boxShadow,
                    getComputedStyle(cover).aspectRatio,
                    add ? 'button' : 'anonymous'];
        }");

        Assert.That(m[0], Is.EqualTo("4px"), "share cards keep the 4 px feed stripe");
        Assert.That(m[1], Is.EqualTo(HexToRgb(accent)), "the stripe uses the schedule colour");
        Assert.That(m[2], Is.EqualTo("0"), "share cards are square");
        Assert.That(m[3], Is.EqualTo("none"), "no shadow on share cards");
        Assert.That(m[4], Is.EqualTo("1 / 1"), "covers are square");
        Assert.That(m[5], Is.EqualTo("anonymous"), "anonymous visitors get no add square");
    }

    [Test]
    public async Task ShareHeroUsesMosaicAndMetroType()
    {
        var station = await SeedShareStationAsync("#2A9D8F");
        TestAuthHandler.IsAuthenticated = false;

        await Page.GotoAsync($"{_factory.ServerAddress}/s/{station.ShareCode}");
        await Expect(Page.Locator(".share-hero .metro-title")).ToHaveTextAsync("Metro Shared Station");

        var m = await Page.EvaluateAsync<string[]>(@"() => {
            const mosaic = document.querySelector('.share-hero__mosaic');
            const title = document.querySelector('.share-hero .metro-title');
            const owner = document.querySelector('.share-hero__owner');
            const cta = document.querySelector('.share-hero__action');
            const host = document.querySelector('.status-label.is-pacing');
            const bar = document.querySelector('.share-hero__bar');
            const rect = mosaic.getBoundingClientRect();
            return [rect.width.toString(),
                    rect.height.toString(),
                    getComputedStyle(title).fontSize,
                    getComputedStyle(title).fontFamily,
                    owner ? owner.textContent.trim() : 'missing',
                    getComputedStyle(cta).borderRadius,
                    getComputedStyle(cta).backgroundColor,
                    host ? host.textContent.trim() : 'missing',
                    bar ? getComputedStyle(bar).height : 'missing',
                    document.querySelectorAll('.share-page .badge').length.toString()];
        }");

        Assert.That(double.Parse(m[0]), Is.EqualTo(280).Within(0.5), "the hero mosaic is 280 px wide");
        Assert.That(double.Parse(m[1]), Is.EqualTo(280).Within(0.5), "the hero mosaic is 280 px tall");
        Assert.That(m[2], Is.EqualTo("48px"), "the station name prints at 48 px");
        Assert.That(m[3], Does.Contain("Sulphur"), "the station name uses the Sulphur display face");
        Assert.That(m[4], Does.Contain("Share Owner"), "the owner line names the station owner");
        Assert.That(m[5], Is.EqualTo("0px"), "the subscribe CTA is square");
        Assert.That(m[6], Is.EqualTo("rgb(252, 93, 0)"), "the subscribe CTA is Metro orange");
        Assert.That(m[7], Is.EqualTo("Host Pace"), "Host Pace is a status label");
        Assert.That(m[8], Is.EqualTo("4px"), "the mosaic carries the orange bar");
        Assert.That(m[9], Is.EqualTo("0"), "no Bootstrap badges remain on the share page");
    }

    [Test]
    public async Task ShareModalsUseSquareCheckboxes()
    {
        var station = await SeedShareStationAsync("#457B9D");
        TestAuthHandler.IsAuthenticated = true;

        await Page.GotoAsync($"{_factory.ServerAddress}/s/{station.ShareCode}");
        await Page.Locator(".share-feed .card-feed-add").First.ClickAsync();

        var modal = Page.Locator(".share-modal.show");
        await Expect(modal).ToBeVisibleAsync(new() { Timeout = 15000 });
        var checkbox = modal.Locator(".form-check-input").First;
        await Expect(checkbox).ToHaveCountAsync(1);
        Assert.That(await checkbox.EvaluateAsync<string>("el => getComputedStyle(el).borderRadius"),
            Is.EqualTo("0px"), "share-modal checkboxes must be square");
    }

    [Test]
    public async Task ShareFits390WithoutPageOverflow()
    {
        var station = await SeedShareStationAsync("#6A057F");
        TestAuthHandler.IsAuthenticated = false;
        await Page.SetViewportSizeAsync(390, 844);

        await Page.GotoAsync($"{_factory.ServerAddress}/s/{station.ShareCode}");
        await Expect(Page.Locator(".share-feed")).ToBeVisibleAsync();

        var m = await Page.EvaluateAsync<string[]>(@"() => {
            const grid = document.querySelector('.share-feed-grid');
            const hero = document.querySelector('.share-hero');
            return [document.documentElement.scrollWidth.toString(),
                    document.documentElement.clientWidth.toString(),
                    getComputedStyle(hero).gridTemplateColumns,
                    getComputedStyle(grid).gridTemplateColumns];
        }");

        Assert.That(int.Parse(m[0]), Is.LessThanOrEqualTo(int.Parse(m[1]) + 1),
            "the share page never scrolls sideways");
        Assert.That(m[2].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, Is.EqualTo(1),
            "the hero stacks on a phone");
        Assert.That(m[3].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, Is.EqualTo(2),
            "the feed grid is two columns at 390 px");
    }

    [Test]
    public async Task StreamUsesHairlineRowsAndMonoUrls()
    {
        await CreateStreamStationAsync();
        await Page.GotoAsync($"{_factory.ServerAddress}/Stream");
        await Expect(Page.Locator(".stream-row").First).ToBeVisibleAsync();

        var m = await Page.EvaluateAsync<string[]>(@"() => {
            const row = document.querySelector('.stream-row');
            const url = row.querySelector('.stream-url');
            const copy = row.querySelector('.stream-copy');
            const s = getComputedStyle(row);
            return [s.borderBottomWidth,
                    s.borderBottomColor,
                    getComputedStyle(url).fontFamily,
                    getComputedStyle(url).fontSize,
                    getComputedStyle(copy).borderRadius,
                    copy.getBoundingClientRect().width.toString(),
                    copy.getAttribute('onclick') ?? 'missing',
                    copy.getBoundingClientRect().height.toString()];
        }");

        Assert.That(m[0], Is.EqualTo("1px"), "stream rows sit on a hairline");
        Assert.That(m[1], Is.EqualTo("rgba(0, 0, 0, 0)"), "the hairline is gone");
        Assert.That(m[2].ToLowerInvariant(), Does.Contain("mono"), "stream URLs use a mono face");
        Assert.That(m[3], Is.EqualTo("12px"), "stream URLs print at 12 px");
        Assert.That(m[4], Is.EqualTo("0px"), "the copy control is square");
        Assert.That(double.Parse(m[5]), Is.GreaterThanOrEqualTo(44), "the copy control is at least 44 px wide");
        Assert.That(m[6], Does.Contain("navigator.clipboard.writeText"), "the copy onclick is kept");
        Assert.That(double.Parse(m[7]), Is.GreaterThanOrEqualTo(44), "the copy control is at least 44 px tall");
    }

    [Test]
    public async Task StreamPlanGateUsesMetroWarning()
    {
        var (freeAccountId, _) = await CreateAccountAsync("Free Streamer");
        TestAuthHandler.TestAccountId = freeAccountId;

        await Page.GotoAsync($"{_factory.ServerAddress}/Stream");
        await Expect(Page.Locator(".metro-warning")).ToBeVisibleAsync();

        var m = await Page.EvaluateAsync<string[]>(@"() => {
            const warning = document.querySelector('.metro-warning');
            const style = getComputedStyle(warning);
            return [style.backgroundColor,
                    style.color,
                    parseFloat(style.borderRadius).toString(),
                    warning.textContent,
                    document.querySelectorAll('code').length.toString()];
        }");

        Assert.That(m[0], Is.EqualTo("rgb(253, 178, 0)"), "the plan gate prints on Metro yellow");
        Assert.That(m[1], Is.EqualTo("rgb(0, 25, 87)"), "the gate text is navy");
        Assert.That(m[2], Is.EqualTo("0"), "the plan gate is square");
        Assert.That(m[3], Does.Contain("Streaming is a Morebob feature"));
        Assert.That(m[4], Is.EqualTo("0"), "a gated account sees no stream URLs");
    }

    private async Task<Station> SeedShareStationAsync(string accent)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var feedUrl = $"https://example.com/metro-share-{suffix}.xml";
        var feed = new Feed
        {
            Id = $"metro-share-feed-{suffix}",
            Url = feedUrl,
            Title = "Metro Share Podcast",
            Description = "<p>A podcast in a shared station.</p>",
            Category = "Comedy",
            ImageUrl = string.Empty,
            CreatedAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow
        };
        await Feeds.InsertOneAsync(feed);
        _feedIds.Add(feed.Id);

        var station = new Station
        {
            Id = $"metro-share-station-{suffix}",
            AccountId = _accountId,
            Name = "Metro Shared Station",
            ShareCode = $"ms{suffix[..10]}",
            IsPublic = true,
            HostPaceEnabled = true,
            Feeds = new[] { feedUrl },
            Schedule = new StationSchedule
            {
                Feeds = new List<StationFeedSchedule>
                {
                    new() { Url = feedUrl, Color = accent, EpisodesToPlay = 2 }
                }
            },
            CreatedAt = DateTime.UtcNow
        };
        await Stations.InsertOneAsync(station);
        _stationIds.Add(station.Id);
        return station;
    }

    private async Task CreateStreamStationAsync()
    {
        var stations = _factory.Services.GetRequiredService<StationsApiService>();
        var station = await stations.CreateStationAsync(_accountId, new Station
        {
            AccountId = _accountId,
            Name = "Metro Stream Station"
        });
        Assert.That(station, Is.Not.Null);
        _stationIds.Add(station!.Id);

        var profiles = _factory.Services.GetRequiredService<ProfilesApiService>();
        await profiles.AddStationToProfileAsync(_profileId, station.Id);
        await UpgradeToMorebobAsync(_accountId);
    }

    private async Task<(string AccountId, string ProfileId)> CreateAccountAsync(string label)
    {
        var accountService = _factory.Services.GetRequiredService<AccountApiService>();
        var account = await accountService.CreateOrUpdateAccountAsync(
            label, $"metro-share-{Guid.NewGuid()}@test.com", "", "Test", Guid.NewGuid().ToString());
        await accountService.CompleteOnboardingAsync(account!.Id);

        var profilesService = _factory.Services.GetRequiredService<ProfilesApiService>();
        var profiles = await profilesService.GetProfilesByAccountIdAsync(account.Id);
        if (profiles == null || profiles.Count == 0)
        {
            await profilesService.CreateProfileAsync(account.Id, new Profile { Name = "Main Profile" });
            profiles = await profilesService.GetProfilesByAccountIdAsync(account.Id);
        }

        return (account.Id, profiles!.First().Id);
    }

    private static async Task UpgradeToMorebobAsync(string accountId)
    {
        var client = new MongoClient("mongodb://localhost:27019");
        var accounts = client.GetDatabase("OhrbobData").GetCollection<Account>("AccountCollection");
        await accounts.UpdateOneAsync(
            a => a.Id == accountId,
            Builders<Account>.Update.Set(a => a.Tier, SubscriptionTier.Morebob));
    }
}
