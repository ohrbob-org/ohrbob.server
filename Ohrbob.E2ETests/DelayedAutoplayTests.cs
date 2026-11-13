using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using Ohrbob.E2ETests.Infrastructure;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Services;

namespace Ohrbob.E2ETests;

/// <summary>
/// Automatic playback (page load / sync / recovery) counts down before it starts, so the
/// user can stop it and pause globally. Explicit play actions start immediately.
/// </summary>
[NonParallelizable]
public class DelayedAutoplayTests : PageTest
{
    // The testcasts host is not resolvable from the browser, so record play() attempts and
    // resolve them; the assertions are about playback intent.
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
        await Page.AddInitScriptAsync(MockPlayScript);

        var accountService = _factory.Services.GetRequiredService<AccountApiService>();
        var account = await accountService.CreateOrUpdateAccountAsync(
            "Delayed Auto Play", $"delayed-{Guid.NewGuid()}@test.com", "", "Test", Guid.NewGuid().ToString());
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
    public async Task AutoPlay_CountsDown_AndGlobalPauseStopsIt()
    {
        await StartStationAsync();

        // A reload is an automatic start: countdown, no playback yet.
        await Page.ReloadAsync();
        var overlay = Page.Locator("#ohrbob-auto-start.show");
        await Expect(overlay).ToBeVisibleAsync(new() { Timeout = 20000 });
        await Expect(overlay.Locator(".ohrbob-auto-start-count")).ToHaveTextAsync(new Regex("^[1-5]$"));
        Assert.That(await PlayCallsAsync(), Is.EqualTo(0), "Auto-play must wait for the countdown");

        await overlay.Locator(".js-auto-start-pause").ClickAsync();
        await Expect(overlay).ToBeHiddenAsync();
        await Page.WaitForTimeoutAsync(6000);
        Assert.That(await PlayCallsAsync(), Is.EqualTo(0), "Global pause must stop the auto-play");
        await Expect(overlay).ToBeHiddenAsync();
        Assert.That(await Page.EvaluateAsync<string>("() => document.cookie"), Does.Contain("ohrbob_user_paused=1"));
    }

    [Test]
    public async Task AutoPlay_StartsWhenTheCountdownElapses()
    {
        await StartStationAsync();

        await Page.ReloadAsync();
        var overlay = Page.Locator("#ohrbob-auto-start.show");
        await Expect(overlay).ToBeVisibleAsync(new() { Timeout = 20000 });

        // The timer bar shrinks while the countdown runs.
        var fill = overlay.Locator(".ohrbob-auto-start-bar-fill");
        await Expect(fill).ToHaveCountAsync(1);
        var widthBefore = await fill.EvaluateAsync<double>("el => el.getBoundingClientRect().width");
        await Page.WaitForTimeoutAsync(1200);
        var widthAfter = await fill.EvaluateAsync<double>("el => el.getBoundingClientRect().width");
        Assert.That(widthAfter, Is.LessThan(widthBefore), "The countdown bar must shrink");

        // Nothing starts while the prompt is counting down (the periodic sync must not
        // bypass the timer).
        await Page.WaitForTimeoutAsync(3000);
        Assert.That(await PlayCallsAsync(), Is.EqualTo(0), "Playback must not start before the countdown ends");
        await Expect(overlay).ToBeVisibleAsync();

        await Page.WaitForFunctionAsync("() => window.__playCalls > 0", null, new() { Timeout = 12000 });
        await Expect(overlay).ToBeHiddenAsync();
    }

    [Test]
    public async Task ExplicitPlay_DoesNotWaitForTheCountdown()
    {
        var station = await StartStationAsync();

        // Clicking the play button again after a reload must start at once.
        await Page.ReloadAsync();
        await Expect(Page.Locator("#ohrbob-auto-start.show")).ToBeVisibleAsync(new() { Timeout = 20000 });
        await Page.Locator("#ohrbob-player .js-play").ClickAsync();
        await Page.WaitForFunctionAsync("() => window.__playCalls > 0", null, new() { Timeout = 5000 });
        await Expect(Page.Locator("#ohrbob-auto-start.show")).ToBeHiddenAsync();
        Assert.That(station, Is.Not.Null);
    }

    private async Task<Station> StartStationAsync()
    {
        var slug = $"{Guid.NewGuid():N}";
        await TestCasts.ConfigureAsync(new TestCastPodcast(slug, "Delayed Feed", new[]
        {
            new TestCastEpisode("Delayed Episode One", $"{slug}-1.mp3"),
            new TestCastEpisode("Delayed Episode Two", $"{slug}-2.mp3"),
        }));

        var stations = _factory.Services.GetRequiredService<StationsApiService>();
        var station = await stations.CreateStationAsync(_accountId, new Station
        {
            AccountId = _accountId,
            Name = "Delayed Station",
            Feeds = new[] { TestCasts.FeedUrl(slug) }
        });
        var profiles = _factory.Services.GetRequiredService<ProfilesApiService>();
        await profiles.AddStationToProfileAsync(_profileId, station!.Id);
        await WaitForEpisodesAsync(station.Id, 2);

        await Page.GotoAsync(_factory.ServerAddress);
        await Page.Locator(".card-station-play").First.ClickAsync();
        await Page.WaitForFunctionAsync("() => window.__playCalls > 0", null, new() { Timeout = 20000 });
        return station;
    }

    private Task<int> PlayCallsAsync() => Page.EvaluateAsync<int>("() => window.__playCalls || 0");

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
