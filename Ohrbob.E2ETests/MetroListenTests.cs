using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using Ohrbob.E2ETests.Infrastructure;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Services;

namespace Ohrbob.E2ETests;

/// <summary>
/// Task 5 of the Metro restyle: the authenticated Listen page is a flat Metro header
/// plus a station grid of square tiles, with a visible podcast count and a single-line
/// 27 px station title.
/// </summary>
[NonParallelizable]
public class MetroListenTests : PageTest
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
            "Metro Listen", $"metro-listen-{Guid.NewGuid()}@test.com", "", "Test", Guid.NewGuid().ToString());
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
    public async Task Listen_UsesMetroStationGrid()
    {
        if (!await TestCasts.IsAvailableAsync())
        {
            Assert.Ignore($"podcast-testcasts is not reachable at {TestCasts.ConfigureBaseUrl}.");
        }

        var slug = $"{Guid.NewGuid():N}";
        await TestCasts.ConfigureAsync(new TestCastPodcast(slug, "Metro Listen Feed", new[]
        {
            new TestCastEpisode("Metro Listen Episode", $"{slug}-1.mp3"),
        }));
        await CreateStationAsync("Metro Listen Station", new[] { TestCasts.FeedUrl(slug) });

        await Page.GotoAsync(_factory.ServerAddress);
        var card = Page.Locator(".card-station").First;
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(Page.Locator(".station-grid")).ToBeVisibleAsync();
        await Expect(Page.Locator(".page-head .metro-title")).ToBeVisibleAsync();

        // The count stays visible and the old Bootstrap badges are gone.
        await Expect(card.Locator(".card-station-count")).ToContainTextAsync("1 podcast");
        await Expect(card.Locator(".badge")).ToHaveCountAsync(0);

        var m = await Page.EvaluateAsync<double[]>(@"() => {
            const card = document.querySelector('.card-station');
            const title = getComputedStyle(card.querySelector('.card-station-title'));
            const play = getComputedStyle(card.querySelector('.card-station-play'));
            const count = getComputedStyle(card.querySelector('.card-station-count'));
            return [ parseFloat(getComputedStyle(card).borderRadius),
                     parseFloat(title.fontSize), title.whiteSpace === 'nowrap' ? 1 : 0,
                     parseFloat(play.width), count.display === 'none' ? 0 : 1 ];
        }");
        Assert.That(m[0], Is.EqualTo(0), "tile radius");
        Assert.That(m[1], Is.GreaterThanOrEqualTo(27), "tile title");
        Assert.That(m[2], Is.EqualTo(1), "tile title truncates");
        Assert.That(m[3], Is.GreaterThanOrEqualTo(44), "play target");
        Assert.That(m[4], Is.EqualTo(1), "podcast count stays visible");
    }

    [Test]
    public async Task Listen_LongStationNameStaysOnOneLine()
    {
        // No feed needed: the station is rendered as an empty tile.
        var longName = new string('W', 90);
        await CreateStationAsync(longName, Array.Empty<string>(), waitForEpisodes: false);

        foreach (var width in new[] { 320, 390 })
        {
            await Page.SetViewportSizeAsync(width, 844);
            await Page.GotoAsync(_factory.ServerAddress);

            var title = Page.Locator(".card-station-title").First;
            await Expect(title).ToBeVisibleAsync(new() { Timeout = 15000 });
            await Expect(title).ToHaveAttributeAsync("title", longName);

            // The single-line title is clipped inside the card instead of pushing it wider.
            var ok = await Page.EvaluateAsync<bool>(@"() => {
                const t = document.querySelector('.card-station-title');
                const card = t.closest('.card-station');
                const cs = getComputedStyle(t);
                const lineHeight = parseFloat(cs.lineHeight) || parseFloat(cs.fontSize);
                return cs.whiteSpace === 'nowrap' &&
                       cs.textOverflow === 'ellipsis' &&
                       t.scrollWidth > t.clientWidth &&
                       t.getBoundingClientRect().right <= card.getBoundingClientRect().right + 1 &&
                       t.getBoundingClientRect().height <= lineHeight * 1.5 &&
                       document.documentElement.scrollWidth <= window.innerWidth + 1;
            }");
            Assert.That(ok, Is.True,
                $"At {width}px the long station name must stay on one clipped line inside the tile");
        }
    }

    [Test]
    public async Task Stations_IndexAndCreateUseMetroLayout()
    {
        if (!await TestCasts.IsAvailableAsync())
        {
            Assert.Ignore($"podcast-testcasts is not reachable at {TestCasts.ConfigureBaseUrl}.");
        }

        // The index mirrors the Listen grid and carries Edit/Delete in a hairline footer.
        await Page.GotoAsync($"{_factory.ServerAddress}/Stations");
        await Expect(Page.Locator(".page-head .metro-title")).ToContainTextAsync("stations");

        await Page.ClickAsync("text=new station");
        await Expect(Page.Locator(".form-narrow")).ToBeVisibleAsync();
        await Expect(Page.Locator(".feed-item")).ToHaveCountAsync(1);

        // The create form keeps the dynamic feed rows.
        await Page.ClickAsync("#addFeedBtn");
        await Expect(Page.Locator(".feed-item")).ToHaveCountAsync(2);
        await Page.Locator(".feed-item").Last.Locator(".remove-feed").ClickAsync();
        await Expect(Page.Locator(".feed-item")).ToHaveCountAsync(1);

        await Page.FillAsync("input[name='Name']", "Metro Manage Station");
        var slug = $"{Guid.NewGuid():N}";
        await TestCasts.ConfigureAsync(new TestCastPodcast(slug, "Metro Manage Feed", new[]
        {
            new TestCastEpisode("Metro Manage Episode", $"{slug}-1.mp3"),
        }));
        await Page.FillAsync("input[name='Feeds']", TestCasts.FeedUrl(slug));
        await Page.ClickAsync("input[type='submit']");
        await Page.WaitForURLAsync("**/Stations", new() { Timeout = 15000 });

        var card = Page.Locator(".card-station--manage", new() { HasTextString = "Metro Manage Station" }).First;
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(Page.Locator(".station-grid")).ToBeVisibleAsync();
        await Expect(card.Locator(".card-station-title")).ToContainTextAsync("Metro Manage Station");
        await Expect(card.Locator(".card-station-footer a", new() { HasTextString = "Edit" })).ToBeVisibleAsync();
        await Expect(card.Locator(".card-station-footer button", new() { HasTextString = "Delete" })).ToBeVisibleAsync();
    }

    private async Task<Station> CreateStationAsync(string name, string[] feedUrls, bool waitForEpisodes = true)
    {
        var stations = _factory.Services.GetRequiredService<StationsApiService>();
        var station = await stations.CreateStationAsync(_accountId, new Station
        {
            AccountId = _accountId,
            Name = name,
            Feeds = feedUrls
        });

        var profileService = _factory.Services.GetRequiredService<ProfilesApiService>();
        await profileService.AddStationToProfileAsync(_profileId, station!.Id);

        if (waitForEpisodes)
        {
            await WaitForEpisodesAsync(station.Id, 1);
        }

        return station;
    }

    private async Task WaitForEpisodesAsync(string stationId, int minimum)
    {
        var stations = _factory.Services.GetRequiredService<StationsApiService>();
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var details = await stations.GetStationDetailsByIdAsync(stationId);
            var count = details?.Feeds?.Sum(f => f.EpisodeCount) ?? 0;
            if (count >= minimum) return;
            await Task.Delay(500);
        }
        Assert.Fail($"Feed episodes were not parsed within the timeout (station {stationId}).");
    }
}
