using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;
using Ohrbob.E2ETests.Infrastructure;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Services;

namespace Ohrbob.E2ETests;

/// <summary>
/// Issue #4: the Edit view's "Resolved Episodes" playlist must show the whole station, not a
/// truncated window. A feed with more episodes than the old 50-item cap must render every one.
/// </summary>
[NonParallelizable]
public class StationPlaylistTests : PageTest
{
    private PlaywrightWebApplicationFactory _factory = null!;
    private string _accountId = string.Empty;

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        IgnoreHTTPSErrors = true,
        // The Edit view hides the tab nav and shows both panes side by side at >=992px,
        // so use a mobile viewport to exercise the "Resolved Episodes" tab.
        ViewportSize = new ViewportSize { Width = 375, Height = 812 }
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
            "Station Playlist", $"station-playlist-{Guid.NewGuid()}@test.com", "", "Test", Guid.NewGuid().ToString());
        await accountService.CompleteOnboardingAsync(account!.Id);
        _accountId = account.Id;

        var profileService = _factory.Services.GetRequiredService<ProfilesApiService>();
        var profiles = await profileService.GetProfilesByAccountIdAsync(_accountId);
        if (profiles == null || profiles.Count == 0)
        {
            await profileService.CreateProfileAsync(_accountId, new Profile { Name = "Main Profile" });
        }

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
    public async Task Edit_Shows_All_Episodes_For_A_Large_Feed()
    {
        // Well over the old 50-item cap.
        const int episodeCount = 75;
        var slug = $"large-{Guid.NewGuid():N}";
        var episodes = Enumerable.Range(0, episodeCount)
            .Select(i => new TestCastEpisode($"Episode {i:D2}", $"{slug}-{i:D2}.mp3"))
            .ToArray();
        await TestCasts.ConfigureAsync(new TestCastPodcast(slug, "Large Feed", episodes));

        var stations = _factory.Services.GetRequiredService<StationsApiService>();
        var station = await stations.CreateStationAsync(_accountId, new Station
        {
            AccountId = _accountId,
            Name = "Large Playlist Station",
            Feeds = new[] { TestCasts.FeedUrl(slug) }
        });

        await WaitForEpisodesAsync(station!.Id, episodeCount);

        await Page.GotoAsync($"{_factory.ServerAddress}/Stations/Edit/{station.Id}");
        await Expect(Page.Locator("h1")).ToContainTextAsync("Edit Station", new() { Timeout = 15000 });

        await Page.ClickAsync("#resolved-tab");
        await Expect(Page.Locator("#resolvedList .resolved-item")).ToHaveCountAsync(
            episodeCount, new() { Timeout = 15000 });
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
