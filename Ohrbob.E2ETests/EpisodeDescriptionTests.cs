using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;
using Ohrbob.E2ETests.Infrastructure;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Services;

namespace Ohrbob.E2ETests;

/// <summary>
/// Issue #3: feed descriptions are HTML and are commonly entity-encoded inside a CDATA
/// section (e.g. <c>R&amp;#252;cktritt</c>), which the XML parser leaves untouched. The
/// station's Edit view must show the decoded plain text (umlauts/special characters)
/// rather than the raw <c>&amp;#...</c> entities.
///
/// The feed is served by the deterministic <c>podcast-testcasts</c> host, which
/// XML-escapes the configured description — exactly reproducing the CDATA/entity shape.
/// Address selection matches <see cref="PlaybackOrderTests"/>: load fixtures via
/// <c>OHRBOB_TESTCASTS_URL</c> and store feed URLs via <c>OHRBOB_TESTCASTS_FEED_URL</c>.
/// </summary>
[NonParallelizable]
public class EpisodeDescriptionTests : PageTest
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
            "Episode Description", $"episode-desc-{Guid.NewGuid()}@test.com", "", "Test", Guid.NewGuid().ToString());
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
    public async Task Edit_Shows_Decoded_Description_For_Entity_Encoded_Feed()
    {
        var slug = $"entity-{Guid.NewGuid():N}";
        await TestCasts.ConfigureAsync(new TestCastPodcast(slug, "Entity Feed", new[]
        {
            new TestCastEpisode(
                "Entity Episode",
                $"{slug}-ep1.mp3",
                "R&#252;cktritt von B&#228;rbel &amp; gr&#246;&#223;te")
        }));

        var stations = _factory.Services.GetRequiredService<StationsApiService>();
        var station = await stations.CreateStationAsync(_accountId, new Station
        {
            AccountId = _accountId,
            Name = "Entity Description Station",
            Feeds = new[] { TestCasts.FeedUrl(slug) }
        });

        // Wait until the API's background worker has parsed the feed's episode.
        await WaitForEpisodesAsync(station!.Id, 1);

        await Page.GotoAsync($"{_factory.ServerAddress}/Stations/Edit/{station.Id}");
        await Expect(Page.Locator("h1")).ToContainTextAsync("Edit Station", new() { Timeout = 15000 });

        // The upcoming playlist (and its episode descriptions) lives on the "Resolved" tab.
        await Page.ClickAsync("#resolved-tab");

        var description = Page.Locator(".resolved-item p.text-muted").First;
        await Expect(description).ToContainTextAsync("Rücktritt von Bärbel & größte", new() { Timeout = 15000 });

        // And the raw entities must not leak into the rendered text.
        var text = await description.InnerTextAsync();
        Assert.That(text, Does.Not.Contain("&#"), "HTML entities must be decoded before display");
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
