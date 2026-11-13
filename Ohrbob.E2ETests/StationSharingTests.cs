using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using MongoDB.Driver;
using NUnit.Framework;
using Ohrbob.E2ETests.Infrastructure;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Services;

namespace Ohrbob.E2ETests;

[NonParallelizable]
public class StationSharingTests : PageTest
{
    private PlaywrightWebApplicationFactory _factory = null!;
    private string _ownerAccountId = string.Empty;
    private string _ownerProfileId = string.Empty;

    public override BrowserNewContextOptions ContextOptions() => new() { IgnoreHTTPSErrors = true };

    [SetUp]
    public async Task Setup()
    {
        await Context.ClearCookiesAsync();
        TestAuthHandler.IsAuthenticated = true;

        _factory = new PlaywrightWebApplicationFactory();
        Page.Console += (_, msg) => Console.WriteLine($"BROWSER CONSOLE: {msg.Text}");
        Page.PageError += (_, msg) => Console.WriteLine($"BROWSER ERROR: {msg}");

        (_ownerAccountId, _ownerProfileId) = await CreateAccountAsync("Owner");
        TestAuthHandler.TestAccountId = _ownerAccountId;
        _factory.CreateClient();
    }

    [TearDown]
    public async Task TearDown()
    {
        await Context.CloseAsync();
        _factory?.Dispose();
        TestAuthHandler.TestAccountId = string.Empty;
        TestAuthHandler.IsAuthenticated = true;
    }

    [Test]
    public async Task Owner_CanMakeStationPublic_AndAnonymousCanViewIt()
    {
        var station = await CreateStationAsync(_ownerAccountId, _ownerProfileId, "Shared Station", isPublic: false);

        await Page.GotoAsync($"{_factory.ServerAddress}/Stations/Edit/{station.Id}");
        await Expect(Page.Locator("h1")).ToContainTextAsync("Edit Station");
        await Expect(Page.Locator("#shareVisibilitySelect")).ToHaveValueAsync("private");

        // The inline visibility dropdown persists immediately.
        await Page.SelectOptionAsync("#shareVisibilitySelect", "public");
        await Expect(Page.Locator("#shareVisibilitySelect")).ToBeEnabledAsync(new() { Timeout = 10000 });
        var shareUrl = await Page.InputValueAsync("#shareStationUrl");
        Assert.That(shareUrl, Does.Contain("/s/"));

        // Opening Station Settings shows the synced switch.
        await Page.ClickAsync("#renameStationBtn");
        await Expect(Page.Locator("#isPublicInput")).ToBeCheckedAsync();

        // Saving the settings switch syncs the inline dropdown back to private.
        await Page.UncheckAsync("#isPublicInput");
        await Page.ClickAsync("#saveStationNameBtn");
        await Expect(Page.Locator("#renameStationModal")).ToBeHiddenAsync(new() { Timeout = 10000 });
        await Expect(Page.Locator("#shareVisibilitySelect")).ToHaveValueAsync("private");

        // Anonymous visitors see the read-only overview with exactly one Subscribe action.
        await SetStationPublicAsync(station.Id, true, _ownerAccountId);
        TestAuthHandler.IsAuthenticated = false;
        await Page.GotoAsync(shareUrl);
        await Expect(Page.Locator("h1")).ToContainTextAsync("Shared Station");
        await Expect(Page.Locator("a.btn:has-text('Subscribe')")).ToHaveCountAsync(1);
        await Expect(Page.Locator("a[href*='/Stations/Edit/']")).ToHaveCountAsync(0);

        // Switching back to private hides the public page again.
        TestAuthHandler.IsAuthenticated = true;
        await SetStationPublicAsync(station.Id, false, _ownerAccountId);
        var response = await Page.GotoAsync(shareUrl);
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Status, Is.EqualTo(404));
    }

    [Test]
    public async Task Subscriber_ChoosesProfile_SeesStationInGrid_AndUnsubscribes()
    {
        var station = await CreateStationAsync(_ownerAccountId, _ownerProfileId, "Friend Station", isPublic: true);

        var (subscriberAccountId, activeProfileId) = await CreateAccountAsync("Friend");
        await UpgradeToMorebobAsync(subscriberAccountId);
        var profilesService = _factory.Services.GetRequiredService<ProfilesApiService>();
        var secondProfile = await profilesService.CreateProfileAsync(
            subscriberAccountId, new Profile { Name = "Second Profile" });
        Assert.That(secondProfile, Is.Not.Null);

        TestAuthHandler.TestAccountId = subscriberAccountId;
        await Page.GotoAsync($"{_factory.ServerAddress}/s/{station.ShareCode}");

        await Page.ClickAsync("button.btn:has-text('Subscribe')");
        await Expect(Page.Locator("#subscribeProfileModal")).ToBeVisibleAsync();
        await Page.CheckAsync($"#subscribe-profile-{secondProfile!.Id}");
        await Page.ClickAsync("#subscribeProfileModal button[type='submit']");

        await Page.WaitForURLAsync($"{_factory.ServerAddress}/", new() { Timeout = 15000 });
        var card = Page.Locator(".card-station", new PageLocatorOptions { HasTextString = "Friend Station" }).First;
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(card.Locator(".badge", new() { HasTextString = "Subscribed" })).ToBeVisibleAsync();
        await Expect(card.Locator("a[href*='/Stations/Edit/']")).ToHaveCountAsync(0);
        Assert.That(secondProfile.Id, Is.Not.EqualTo(activeProfileId), "A non-active profile should have been chosen");

        await card.ClickAsync();
        await Expect(Page.Locator($"#ohrbob-player[data-active-station-id='{station.Id}']"))
            .ToBeVisibleAsync(new() { Timeout = 15000 });

        await card.Locator("button[title='Unsubscribe']").ClickAsync();
        await Expect(Page.Locator("#confirmationModal")).ToBeVisibleAsync();
        await Page.ClickAsync("#confirmationModalConfirmBtn");
        await Expect(Page.Locator(".card-station", new PageLocatorOptions { HasTextString = "Friend Station" })).ToHaveCountAsync(0, new() { Timeout = 15000 });
    }

    [Test]
    public async Task HostPace_SubscriberOnlyGetsEpisodesCompletedByHost()
    {
        if (!await TestCasts.IsAvailableAsync())
            Assert.Ignore($"podcast-testcasts is not reachable at {TestCasts.ConfigureBaseUrl}.");

        var slug = $"host-pace-{Guid.NewGuid():N}";
        await TestCasts.ConfigureAsync(new TestCastPodcast(slug, "Host Pace Feed", new[]
        {
            new TestCastEpisode("Episode A", $"{slug}-a.mp3"),
            new TestCastEpisode("Episode B", $"{slug}-b.mp3"),
            new TestCastEpisode("Episode C", $"{slug}-c.mp3")
        }));

        var stations = _factory.Services.GetRequiredService<StationsApiService>();
        var station = await CreateStationWithFeedsAsync(
            _ownerAccountId, _ownerProfileId, "Host Paced", true, new[] { TestCasts.FeedUrl(slug) });
        await WaitForEpisodesAsync(station.Id, 3);

        var playlist = await stations.GetStationPlaylistAsync(station.Id, _ownerProfileId);
        Assert.That(playlist, Is.Not.Null.And.Count.GreaterThanOrEqualTo(3));
        var first = playlist![0].Episode;
        var second = playlist[1].Episode;

        // The host has completed only the first episode; enable Host Pace through the settings modal.
        await stations.MarkEpisodeCompletedAsync(station.Id, first.Id, _ownerProfileId);
        await Page.GotoAsync($"{_factory.ServerAddress}/Stations/Edit/{station.Id}");
        await Page.ClickAsync("#renameStationBtn");
        await Page.CheckAsync("#hostPaceInput");
        await Page.SelectOptionAsync("#hostProfileSelect", _ownerProfileId);
        await Page.ClickAsync("#saveStationNameBtn");
        await Expect(Page.Locator("#renameStationModal")).ToBeHiddenAsync(new() { Timeout = 10000 });

        await Page.ReloadAsync();
        await Expect(Page.Locator("h1 .badge:has-text('Host Pace')")).ToBeVisibleAsync();

        await Page.GotoAsync($"{_factory.ServerAddress}/s/{station.ShareCode}");
        await Expect(Page.Locator(".badge:has-text('Host Pace')").First).ToBeVisibleAsync();

        var (subscriberAccountId, subscriberProfileId) = await CreateAccountAsync("Paced Listener");
        var profiles = _factory.Services.GetRequiredService<ProfilesApiService>();
        await profiles.SubscribeToStationAsync(subscriberProfileId, station.Id);

        TestAuthHandler.TestAccountId = subscriberAccountId;
        await Page.GotoAsync($"{_factory.ServerAddress}/");
        var card = Page.Locator(".card-station", new PageLocatorOptions { HasTextString = "Host Paced" }).First;
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(card.Locator(".badge:has-text('Host Pace')")).ToBeVisibleAsync();

        await card.ClickAsync();
        await Expect(Page.Locator("#ohrbob-player")).ToHaveAttributeAsync("track", first.Title, new() { Timeout = 15000 });

        // Completing the only released episode makes the station empty for the subscriber.
        await stations.MarkEpisodeCompletedAsync(station.Id, first.Id, subscriberProfileId);
        await Page.ReloadAsync();
        var emptyCard = Page.Locator(".card-station", new PageLocatorOptions { HasTextString = "Host Paced" }).First;
        await Expect(emptyCard.Locator(".badge.bg-danger")).ToBeVisibleAsync(new() { Timeout = 15000 });

        // The host releases the next episode and the subscriber receives it.
        await stations.MarkEpisodeCompletedAsync(station.Id, second.Id, _ownerProfileId);
        await Page.ReloadAsync();
        var releasedCard = Page.Locator(".card-station", new PageLocatorOptions { HasTextString = "Host Paced" }).First;
        await Expect(releasedCard.Locator(".badge.bg-danger")).Not.ToBeVisibleAsync(new() { Timeout = 15000 });
        await releasedCard.ClickAsync();
        await Expect(Page.Locator("#ohrbob-player")).ToHaveAttributeAsync("track", second.Title, new() { Timeout = 15000 });
    }

    [Test]
    public async Task Viewer_CanAddSharedPodcastToOwnStation()
    {
        if (!await TestCasts.IsAvailableAsync())
            Assert.Ignore($"podcast-testcasts is not reachable at {TestCasts.ConfigureBaseUrl}.");

        var slug = $"share-add-{Guid.NewGuid():N}";
        await TestCasts.ConfigureAsync(new TestCastPodcast(slug, "Shared Feed",
            new[] { new TestCastEpisode("Episode 1", $"{slug}-1.mp3") }));

        var station = await CreateStationWithFeedsAsync(
            _ownerAccountId, _ownerProfileId, "Feed Station", true, new[] { TestCasts.FeedUrl(slug) });

        var (viewerAccountId, viewerProfileId) = await CreateAccountAsync("Adder");
        var target = await CreateStationAsync(viewerAccountId, viewerProfileId, "My Target Station", false);

        TestAuthHandler.TestAccountId = viewerAccountId;
        await Page.GotoAsync($"{_factory.ServerAddress}/s/{station.ShareCode}");

        await Page.Locator("button[title='Add this podcast to a station']").First.ClickAsync();
        await Page.CheckAsync($"input[name='stationIds'][value='{target.Id}']");
        await Page.Locator(".modal.show button[type='submit']:has-text('Confirm Add')").ClickAsync();
        await Expect(Page.Locator(".modal.show")).ToHaveCountAsync(0, new() { Timeout = 15000 });

        var stations = _factory.Services.GetRequiredService<StationsApiService>();
        Station? updated = null;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            updated = await stations.GetStationByIdAsync(target.Id);
            if (updated?.Feeds.Contains(TestCasts.FeedUrl(slug)) == true) break;
            await Task.Delay(250);
        }

        Assert.That(updated, Is.Not.Null);
        Assert.That(updated!.Feeds, Does.Contain(TestCasts.FeedUrl(slug)));
    }

    [Test]
    public async Task PrivateStation_RefusesNewSubscribers_ButExistingSubscriberKeepsAccess()
    {
        var station = await CreateStationAsync(_ownerAccountId, _ownerProfileId, "Private Later", isPublic: true);

        var (subscriberAccountId, subscriberProfileId) = await CreateAccountAsync("Existing Subscriber");
        var profiles = _factory.Services.GetRequiredService<ProfilesApiService>();
        await profiles.SubscribeToStationAsync(subscriberProfileId, station.Id);

        await SetStationPublicAsync(station.Id, false, _ownerAccountId);

        var (newcomerAccountId, _) = await CreateAccountAsync("Newcomer");
        TestAuthHandler.TestAccountId = newcomerAccountId;
        var response = await Page.GotoAsync($"{_factory.ServerAddress}/s/{station.ShareCode}");
        Assert.That(response!.Status, Is.EqualTo(404), "Newcomers cannot open or subscribe to a private station");

        TestAuthHandler.TestAccountId = subscriberAccountId;
        await Page.GotoAsync($"{_factory.ServerAddress}/");
        var card = Page.Locator(".card-station", new PageLocatorOptions { HasTextString = "Private Later" }).First;
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(card.Locator("a[href*='/Stations/Edit/']")).ToHaveCountAsync(0);

        await card.ClickAsync();
        await Expect(Page.Locator($"#ohrbob-player[data-active-station-id='{station.Id}']"))
            .ToBeVisibleAsync(new() { Timeout = 15000 });
    }

    private Task<Station> CreateStationAsync(string accountId, string profileId, string name, bool isPublic) =>
        CreateStationWithFeedsAsync(accountId, profileId, name, isPublic, Array.Empty<string>());

    private async Task<Station> CreateStationWithFeedsAsync(
        string accountId, string profileId, string name, bool isPublic, string[] feedUrls)
    {
        var stations = _factory.Services.GetRequiredService<StationsApiService>();
        var station = await stations.CreateStationAsync(accountId, new Station
        {
            AccountId = accountId,
            Name = name,
            IsPublic = isPublic,
            Feeds = feedUrls
        });
        Assert.That(station, Is.Not.Null);
        Assert.That(station!.ShareCode, Is.Not.Empty);

        var profiles = _factory.Services.GetRequiredService<ProfilesApiService>();
        await profiles.AddStationToProfileAsync(profileId, station.Id);
        return station;
    }

    private async Task WaitForEpisodesAsync(string stationId, int minimum)
    {
        var stations = _factory.Services.GetRequiredService<StationsApiService>();
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var details = await stations.GetStationDetailsByIdAsync(stationId);
            if ((details?.Feeds.Sum(f => f.EpisodeCount) ?? 0) >= minimum) return;
            await Task.Delay(500);
        }
        Assert.Fail($"Feed episodes were not parsed within the timeout (station {stationId}).");
    }

    private async Task<(string AccountId, string ProfileId)> CreateAccountAsync(string label)
    {
        var accountService = _factory.Services.GetRequiredService<AccountApiService>();
        var account = await accountService.CreateOrUpdateAccountAsync(
            $"Share {label}", $"share-{Guid.NewGuid()}@test.com", "", "Test", Guid.NewGuid().ToString());
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

    private async Task SetStationPublicAsync(string stationId, bool isPublic, string accountId)
    {
        var stations = _factory.Services.GetRequiredService<StationsApiService>();
        var station = await stations.GetStationByIdAsync(stationId);
        station!.IsPublic = isPublic;
        await stations.UpdateStationAsync(station);
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
