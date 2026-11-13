using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using Ohrbob.E2ETests.Infrastructure;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Services;

namespace Ohrbob.E2ETests;

/// <summary>
/// Several web sessions share one player state on the server. Starting a station in one
/// session must not make another open session start playing by itself.
/// </summary>
[NonParallelizable]
public class MultiSessionPlaybackTests : PageTest
{
    // The testcasts host is not reachable from the browser in this environment, so media
    // can never buffer. Record play() attempts and resolve them so the assertions are about
    // playback intent, not about the network.
    private const string MockPlayScript = @"window.__playCalls = 0;
        HTMLMediaElement.prototype.play = function () { window.__playCalls++; return Promise.resolve(); };";

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
            "Multi Session", $"multi-session-{Guid.NewGuid()}@test.com", "", "Test", Guid.NewGuid().ToString());
        await accountService.CompleteOnboardingAsync(account!.Id);
        _accountId = account.Id;

        var profiles = _factory.Services.GetRequiredService<ProfilesApiService>();
        var list = await profiles.GetProfilesByAccountIdAsync(_accountId);
        if (list == null || list.Count == 0)
        {
            await profiles.CreateProfileAsync(_accountId, new Profile { Name = "Main Profile" });
            list = await profiles.GetProfilesByAccountIdAsync(_accountId);
        }
        _profileId = list!.First().Id;

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
    public async Task PlayingInOneSession_DoesNotStartPlaybackInAnotherSession()
    {
        var slug = $"{Guid.NewGuid():N}";
        await TestCasts.ConfigureAsync(new TestCastPodcast(slug, "Session Feed", new[]
        {
            new TestCastEpisode("Session Episode One", $"{slug}-1.mp3"),
            new TestCastEpisode("Session Episode Two", $"{slug}-2.mp3"),
        }));

        var stations = _factory.Services.GetRequiredService<StationsApiService>();
        var station = await stations.CreateStationAsync(_accountId, new Station
        {
            AccountId = _accountId,
            Name = "Session Station",
            Feeds = new[] { TestCasts.FeedUrl(slug) }
        });
        var profiles = _factory.Services.GetRequiredService<ProfilesApiService>();
        await profiles.AddStationToProfileAsync(_profileId, station!.Id);
        await WaitForEpisodesAsync(station.Id, 2);

        // Session A starts the station.
        await Page.AddInitScriptAsync(MockPlayScript);
        await Page.SetViewportSizeAsync(1280, 900);
        await Page.GotoAsync(_factory.ServerAddress);
        await Page.Locator(".card-station-play").First.ClickAsync();
        await Expect(Page.Locator("#ohrbob-player")).ToHaveAttributeAsync(
            "data-active-station-id", station.Id, new() { Timeout = 20000 });
        await Expect(Page.Locator("#ohrbob-player")).ToHaveAttributeAsync(
            "track", new Regex("Session Episode (One|Two)"), new() { Timeout = 20000 });
        await Page.WaitForFunctionAsync("() => window.__playCalls > 0", null, new() { Timeout = 10000 });
        Assert.That(await PlayCallsAsync(Page), Is.GreaterThan(0), "Session A must have started playback");

        // Session B opens while A keeps playing.
        var contextB = await Browser.NewContextAsync(new BrowserNewContextOptions { IgnoreHTTPSErrors = true });
        try
        {
            var pageB = await contextB.NewPageAsync();
            pageB.Console += (_, msg) => Console.WriteLine($"BROWSER B CONSOLE: {msg.Text}");
            pageB.PageError += (_, msg) => Console.WriteLine($"BROWSER B ERROR: {msg}");
            await pageB.AddInitScriptAsync(MockPlayScript);
            await pageB.SetViewportSizeAsync(1280, 900);
            await pageB.GotoAsync(_factory.ServerAddress);

            // It still sees the shared state (station + track) ...
            await Expect(pageB.Locator("#ohrbob-player")).ToHaveAttributeAsync(
                "data-active-station-id", station.Id, new() { Timeout = 20000 });
            await Expect(pageB.Locator("#ohrbob-player")).ToHaveAttributeAsync(
                "track", new Regex("Session Episode (One|Two)"), new() { Timeout = 20000 });

            // ... but it never starts playing by itself, not even after several syncs.
            Assert.That(await PlayCallsAsync(pageB), Is.EqualTo(0),
                "Session B must not start playing just because session A is playing");
            await pageB.WaitForTimeoutAsync(7000);
            Assert.That(await PlayCallsAsync(pageB), Is.EqualTo(0),
                "Session B must stay paused while session A is playing");

            // Pressing play in B is an explicit action and starts playback there.
            await pageB.Locator("#ohrbob-player .js-play").ClickAsync();
            await pageB.WaitForFunctionAsync("() => window.__playCalls > 0", null, new() { Timeout = 10000 });
            Assert.That(await PlayCallsAsync(pageB), Is.GreaterThan(0),
                "An explicit play in session B must start playback there");
        }
        finally
        {
            await contextB.CloseAsync();
        }
    }

    private static Task<int> PlayCallsAsync(IPage page) =>
        page.EvaluateAsync<int>("() => window.__playCalls || 0");

    private async Task WaitForEpisodesAsync(string stationId, int minimum)
    {
        var stations = _factory.Services.GetRequiredService<StationsApiService>();
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var details = await stations.GetStationDetailsByIdAsync(stationId);
            if ((details?.Feeds?.Sum(f => f.EpisodeCount) ?? 0) >= minimum) return;
            await Task.Delay(500);
        }
        Assert.Fail($"Feed episodes were not parsed within the timeout (station {stationId}).");
    }
}
