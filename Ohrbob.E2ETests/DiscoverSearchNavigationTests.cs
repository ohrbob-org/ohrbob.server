using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using Ohrbob.E2ETests.Infrastructure;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Web.Services;

namespace Ohrbob.E2ETests;

/// <summary>
/// Discover is reached through an htmx-boosted navigation, so the layout (and its
/// @section Scripts) is not rendered. discover.js still has to run on the swapped
/// content, otherwise the recommendation lanes stay visible and the search results
/// are buried below them — which looks like search does nothing.
/// </summary>
[NonParallelizable]
public class DiscoverSearchNavigationTests : PageTest
{
    private PlaywrightWebApplicationFactory _factory = null!;

    [SetUp]
    public async Task Setup()
    {
        await Context.ClearCookiesAsync();
        _factory = new PlaywrightWebApplicationFactory();

        var accountService = _factory.Services.GetRequiredService<AccountApiService>();
        var account = await accountService.CreateOrUpdateAccountAsync(
            "Discover Nav", $"discover-nav-{Guid.NewGuid()}@test.com", "", "Test", Guid.NewGuid().ToString());
        await accountService.CompleteOnboardingAsync(account!.Id);

        var profileService = _factory.Services.GetRequiredService<ProfilesApiService>();
        var profiles = await profileService.GetProfilesByAccountIdAsync(account.Id);
        if (profiles == null || profiles.Count == 0)
        {
            await profileService.CreateProfileAsync(account.Id, new Profile { Name = "Main Profile" });
        }

        TestAuthHandler.TestAccountId = account.Id;
    }

    [TearDown]
    public async Task TearDown()
    {
        await Context.CloseAsync();
        _factory.Dispose();
        TestAuthHandler.TestAccountId = string.Empty;
    }

    [Test]
    public async Task SearchFiresAfterHtmxNavigationFromListenToDiscover()
    {
        await Page.GotoAsync(_factory.ServerAddress);

        await Page.ClickAsync(".discover-plus-circle");
        var input = Page.Locator("#discover-search");
        await Expect(input).ToBeVisibleAsync(new() { Timeout = 10000 });

        var response = await TypeAndWaitForSearchResponse(input, "Welk");
        Assert.That(response.Status, Is.EqualTo(200));

        // discover.js ran on the swapped content: the lanes are hidden so results show.
        await Expect(Page.Locator("#recommendation-lanes")).ToHaveClassAsync(
            new Regex("\\bd-none\\b"), new() { Timeout = 3000 });

        // The partial was swapped into the results container.
        await Expect(Page.Locator("#search-results")).ToContainTextAsync(
            new Regex("."), new() { Timeout = 5000 });

        // Browser back/forward restores the view from htmx history; search must work there too.
        await Page.GoBackAsync();
        await Expect(Page.Locator("h2")).ToContainTextAsync("Listen", new() { Timeout = 10000 });
        await Page.GoForwardAsync();

        var restoredInput = Page.Locator("#discover-search");
        await Expect(restoredInput).ToBeVisibleAsync(new() { Timeout = 10000 });
        var restoredResponse = await TypeAndWaitForSearchResponse(restoredInput, "Welk");
        Assert.That(restoredResponse.Status, Is.EqualTo(200));
        await Expect(Page.Locator("#search-results")).ToContainTextAsync(
            new Regex("."), new() { Timeout = 5000 });
    }

    private async Task<IResponse> TypeAndWaitForSearchResponse(ILocator input, string query)
    {
        var responseTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/Discover/Search", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 15000 });

        await input.PressSequentiallyAsync(query, new() { Delay = 100 });

        var response = await responseTask;
        Assert.That(response.Url, Does.Contain($"query={query}"));
        return response;
    }
}
