using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using Ohrbob.E2ETests.Infrastructure;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Services;

namespace Ohrbob.E2ETests;

/// <summary>
/// Issue #6: a station whose episodes are all completed has nothing left to play, even though
/// its raw episode count is greater than zero. The Listen view must surface that with the red
/// "Empty" badge on the station card.
/// </summary>
[NonParallelizable]
public class ListenEmptyBadgeTests : PageTest
{
    private PlaywrightWebApplicationFactory _factory = null!;
    private string _accountId = string.Empty;
    private string _profileId = string.Empty;

    public override BrowserNewContextOptions ContextOptions() => new() { IgnoreHTTPSErrors = true };

    [SetUp]
    public async Task Setup()
    {
        await Context.ClearCookiesAsync();

        if (!await TestCasts.IsAvailableAsync())
        {
            Assert.Ignore($"podcast-testcasts is not reachable at {TestCasts.ConfigureBaseUrl}.");
        }

        _factory = new PlaywrightWebApplicationFactory();
        Page.Console += (_, msg) => Console.WriteLine($"BROWSER CONSOLE: {msg.Text}");
        Page.PageError += (_, msg) => Console.WriteLine($"BROWSER ERROR: {msg}");

        var accountService = _factory.Services.GetRequiredService<AccountApiService>();
        var account = await accountService.CreateOrUpdateAccountAsync(
            "Listen Badge", $"listen-badge-{Guid.NewGuid()}@test.com", "", "Test", Guid.NewGuid().ToString());
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
    public async Task ListenView_ShowsEmptyBadge_WhenEveryEpisodeIsCompleted()
    {
        var slug = $"{Guid.NewGuid():N}";
        await TestCasts.ConfigureAsync(new TestCastPodcast(slug, "Badge Feed", new[]
        {
            new TestCastEpisode("Only Episode", $"{slug}-only.mp3"),
        }));

        var station = await CreateStationAsync("Badge Station", new[] { TestCasts.FeedUrl(slug) }, minimumEpisodes: 1);

        // While an episode is left to play, the card has episodes and no "Empty" badge.
        await Page.GotoAsync(_factory.ServerAddress);
        var card = StationCard("Badge Station");
        await card.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20000 });
        await Expect(card.Locator(".card-station-count")).ToContainTextAsync("1 podcast");
        await Expect(card.Locator(".badge.bg-danger")).Not.ToBeVisibleAsync();

        // Complete every episode for the active profile.
        var stationsApi = _factory.Services.GetRequiredService<StationsApiService>();
        var playlist = await stationsApi.GetStationPlaylistAsync(station.Id, _profileId);
        Assert.That(playlist, Is.Not.Null.And.Not.Empty, "The station's episode must be in the playlist");
        foreach (var ep in playlist!)
        {
            await stationsApi.MarkEpisodeCompletedAsync(station.Id, ep.Episode.Id, _profileId);
        }

        // Nothing left to play: the red "Empty" badge must appear even though episodes exist.
        await Page.ReloadAsync();
        var cardAfter = StationCard("Badge Station");
        await cardAfter.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20000 });
        await Expect(cardAfter.Locator(".card-station-count")).ToContainTextAsync("1 podcast");
        var badge = cardAfter.Locator(".badge.bg-danger");
        await Expect(badge).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(badge).ToHaveTextAsync(new Regex("Empty"));
    }

    [Test]
    public async Task CardArtPlaysTheStation_AndTheBodyOpensTheEditor()
    {
        var slug = $"{Guid.NewGuid():N}";
        await TestCasts.ConfigureAsync(new TestCastPodcast(slug, "Split Feed", new[]
        {
            new TestCastEpisode("Split Episode", $"{slug}-1.mp3"),
        }));

        var station = await CreateStationAsync("Split Card Station", new[] { TestCasts.FeedUrl(slug) }, minimumEpisodes: 1);

        await Page.GotoAsync(_factory.ServerAddress);
        var card = StationCard("Split Card Station");
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 20000 });

        // The artwork starts the station's playback.
        await card.Locator(".card-station-play").ClickAsync();
        await Expect(Page.Locator("#ohrbob-player")).ToHaveAttributeAsync(
            "data-active-station-id", station.Id, new() { Timeout = 20000 });

        // The lower section opens the station editor.
        await card.Locator(".card-station-body").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex($"/Stations/Edit/{station.Id}"), new() { Timeout = 20000 });
    }

    private async Task<Station> CreateStationAsync(string name, string[] feedUrls, int minimumEpisodes)
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

        await WaitForEpisodesAsync(station.Id, minimumEpisodes);
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

    private ILocator StationCard(string name) =>
        Page.Locator(".card-station", new PageLocatorOptions { HasTextString = name }).First;
}
