using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;
using Ohrbob.E2ETests.Infrastructure;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Services;

namespace Ohrbob.E2ETests;

/// <summary>
/// Issue #10: every episode in the Edit view's playlist offers a tiny "continue",
/// "reset playhead" and "set playhead" control.
/// </summary>
[NonParallelizable]
public class EpisodePlayheadTests : PageTest
{
    private PlaywrightWebApplicationFactory _factory = null!;
    private StationsApiService _stations = null!;
    private string _accountId = string.Empty;
    private string _profileId = string.Empty;
    private Station _station = null!;

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        IgnoreHTTPSErrors = true,
        ViewportSize = new ViewportSize { Width = 1280, Height = 900 }
    };

    [SetUp]
    public async Task Setup()
    {
        await Context.ClearCookiesAsync();

        if (!await TestCasts.IsAvailableAsync())
        {
            Assert.Ignore(
                $"podcast-testcasts is not reachable at {TestCasts.ConfigureBaseUrl}. Start it via " +
                "\"docker compose up -d testcasts\" (see docker-compose.yml and testdata/ohrbob-testcasts), " +
                "or point OHRBOB_TESTCASTS_URL at a running instance. When the Api runs in Docker, also set " +
                "OHRBOB_TESTCASTS_FEED_URL=http://testcasts:5004 so stored feed URLs use the service name.");
        }

        _factory = new PlaywrightWebApplicationFactory();
        Page.Console += (_, msg) => Console.WriteLine($"BROWSER CONSOLE: {msg.Text}");
        Page.PageError += (_, err) => Console.WriteLine($"BROWSER ERROR: {err}");

        var accountService = _factory.Services.GetRequiredService<AccountApiService>();
        var account = await accountService.CreateOrUpdateAccountAsync(
            "Playhead", $"playhead-{Guid.NewGuid()}@test.com", "", "Test", Guid.NewGuid().ToString());
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

        var slug = $"playhead-{Guid.NewGuid():N}";
        await TestCasts.ConfigureAsync(new TestCastPodcast(slug, "Playhead Feed", new[]
        {
            new TestCastEpisode("Episode One", $"{slug}-1.mp3"),
            new TestCastEpisode("Episode Two", $"{slug}-2.mp3"),
            new TestCastEpisode("Episode Three", $"{slug}-3.mp3")
        }));

        _stations = _factory.Services.GetRequiredService<StationsApiService>();
        _station = (await _stations.CreateStationAsync(_accountId, new Station
        {
            AccountId = _accountId,
            Name = "Playhead Station",
            Feeds = new[] { TestCasts.FeedUrl(slug) }
        }))!;

        TestAuthHandler.TestAccountId = _accountId;
        _factory.CreateClient();

        // Never let the widget auto-play (and overwrite the playhead) during the test.
        await Context.AddCookiesAsync(new[]
        {
            new Cookie { Name = "ohrbob_user_paused", Value = "1", Url = _factory.ServerAddress }
        });
    }

    [TearDown]
    public async Task TearDown()
    {
        await Context.CloseAsync();
        _factory?.Dispose();
        TestAuthHandler.TestAccountId = string.Empty;
    }

    [Test]
    public async Task Edit_Playlist_Can_Reset_And_Set_The_Playhead()
    {
        var episodeId = await WaitForFirstEpisodeAsync();

        await _stations.SetEpisodePlayheadAsync(_station.Id, episodeId, _profileId, 120);

        await Page.GotoAsync($"{_factory.ServerAddress}/Stations/Edit/{_station.Id}");
        await Expect(Page.Locator("h1 #stationNameDisplay")).ToBeVisibleAsync(new() { Timeout = 15000 });

        var row = Page.Locator($".resolved-item[data-episode-id='{episodeId}']");
        await Expect(row).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(row.Locator(".episode-position")).ToHaveTextAsync("120");
        await Expect(row.Locator(".continue-episode-btn")).ToBeVisibleAsync();

        await row.Locator(".reset-playhead-btn").ClickAsync();
        await Expect(row.Locator(".episode-position")).ToHaveTextAsync("0", new() { Timeout = 10000 });
        await ExpectPlayheadAsync(episodeId, 0);

        await row.Locator(".set-playhead-btn").ClickAsync();
        await Expect(Page.Locator("#setPlayheadModal")).ToBeVisibleAsync();
        await Page.FillAsync("#playheadSecondsInput", "42");
        await Page.ClickAsync("#confirmSetPlayheadBtn");

        await Expect(row.Locator(".episode-position")).ToHaveTextAsync("42", new() { Timeout = 10000 });
        await ExpectPlayheadAsync(episodeId, 42);
    }

    [Test]
    public async Task Edit_Playlist_StripsHtmlFromEpisodeTitleAndDescription()
    {
        var slug = $"html-{Guid.NewGuid():N}";
        await TestCasts.ConfigureAsync(new TestCastPodcast(slug, "HTML Feed", new[]
        {
            new TestCastEpisode(
                "Episode <b>Bold</b>",
                $"{slug}-1.mp3",
                "<p>Hello <a href=\"https://example.com\">world</a></p>")
        }));

        var station = (await _stations.CreateStationAsync(_accountId, new Station
        {
            AccountId = _accountId,
            Name = "HTML Station",
            Feeds = new[] { TestCasts.FeedUrl(slug) }
        }))!;

        var episodeId = await WaitForFirstEpisodeAsync(station);

        await Page.GotoAsync($"{_factory.ServerAddress}/Stations/Edit/{station.Id}");
        await Expect(Page.Locator("h1 #stationNameDisplay")).ToBeVisibleAsync(new() { Timeout = 15000 });

        var row = Page.Locator($".resolved-item[data-episode-id='{episodeId}']");
        await Expect(row).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(row.Locator("strong")).ToHaveTextAsync("Episode Bold");
        await Expect(row.Locator("p")).ToHaveTextAsync("Hello world");
    }

    private Task<string> WaitForFirstEpisodeAsync() => WaitForFirstEpisodeAsync(_station);

    private async Task<string> WaitForFirstEpisodeAsync(Station station)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var details = await _stations.GetStationDetailsByIdAsync(station.Id);
            if (details?.Feeds?.Sum(f => f.EpisodeCount) > 0)
            {
                var playlist = await _stations.GetStationPlaylistAsync(station.Id, _profileId);
                var episode = playlist?.FirstOrDefault();
                if (episode != null) return episode.Episode.Id;
            }
            await Task.Delay(500);
        }

        Assert.Fail($"Feed episodes were not parsed within the timeout (station {station.Id}).");
        return string.Empty;
    }

    private async Task ExpectPlayheadAsync(string episodeId, int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var playlist = await _stations.GetStationPlaylistAsync(_station.Id, _profileId);
            if (playlist?.FirstOrDefault(p => p.Episode.Id == episodeId)?.PositionSeconds == expected) return;
            await Task.Delay(200);
        }

        Assert.Fail($"Expected the stored playhead of {episodeId} to be {expected}.");
    }
}
