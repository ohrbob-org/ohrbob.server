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
        var card = Page.Locator(".card", new PageLocatorOptions { HasTextString = "Friend Station" }).First;
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
        await Expect(Page.Locator(".card", new PageLocatorOptions { HasTextString = "Friend Station" })).ToHaveCountAsync(0, new() { Timeout = 15000 });
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
        var card = Page.Locator(".card", new PageLocatorOptions { HasTextString = "Private Later" }).First;
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(card.Locator("a[href*='/Stations/Edit/']")).ToHaveCountAsync(0);

        await card.ClickAsync();
        await Expect(Page.Locator($"#ohrbob-player[data-active-station-id='{station.Id}']"))
            .ToBeVisibleAsync(new() { Timeout = 15000 });
    }

    private async Task<Station> CreateStationAsync(string accountId, string profileId, string name, bool isPublic)
    {
        var stations = _factory.Services.GetRequiredService<StationsApiService>();
        var station = await stations.CreateStationAsync(accountId, new Station
        {
            AccountId = accountId,
            Name = name,
            IsPublic = isPublic,
            Feeds = Array.Empty<string>()
        });
        Assert.That(station, Is.Not.Null);
        Assert.That(station!.ShareCode, Is.Not.Empty);

        var profiles = _factory.Services.GetRequiredService<ProfilesApiService>();
        await profiles.AddStationToProfileAsync(profileId, station.Id);
        return station;
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
