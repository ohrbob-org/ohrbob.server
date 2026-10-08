using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;
using Ohrbob.E2ETests.Infrastructure;
using Ohrbob.Server.Shared.Data;
using Microsoft.Extensions.DependencyInjection;
using System.IO;

namespace Ohrbob.E2ETests;

[NonParallelizable]
public class StationsEditTests : PageTest
{
    private PlaywrightWebApplicationFactory _factory = null!;
    private string _testAccountId = string.Empty;

    public override BrowserNewContextOptions ContextOptions()
    {
        return new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true
        };
    }

    [SetUp]
    public async Task Setup()
    {
        _factory = new PlaywrightWebApplicationFactory();
        
        var accountService = _factory.Services.GetRequiredService<Ohrbob.Web.Services.AccountApiService>();
        var uniqueEmail = $"stations-{Guid.NewGuid()}@test.com";
        var account = await accountService.CreateOrUpdateAccountAsync("Stations Test", uniqueEmail, "", "Test", Guid.NewGuid().ToString());
        
        if (account != null) {
            await accountService.CompleteOnboardingAsync(account.Id);
        }
        
        _testAccountId = account!.Id;
        TestAuthHandler.TestAccountId = _testAccountId;

        _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _factory.Dispose();
    }

    [Test]
    public async Task Can_Mark_Episode_Completed_From_Edit_Page()
    {
        var url = _factory.ServerAddress;
        
        // Ensure profile exists
        var profileService = _factory.Services.GetRequiredService<Ohrbob.Web.Services.ProfilesApiService>();
        var profiles = await profileService.GetProfilesByAccountIdAsync(_testAccountId);
        if (profiles == null || !profiles.Any())
        {
            await profileService.CreateProfileAsync(_testAccountId, new Profile { Name = "Main Profile" });
        }
        
        Page.Console += (_, msg) => Console.WriteLine($"BROWSER CONSOLE: {msg.Text}");
        Page.PageError += (_, msg) => Console.WriteLine($"BROWSER ERROR: {msg}");
        await Page.GotoAsync(url);
        
        var feedPath = Path.Combine(Directory.GetCurrentDirectory(), "../../../../example_feeds.txt");
        if (!File.Exists(feedPath))
            feedPath = Path.Combine(Directory.GetCurrentDirectory(), "../../../../../example_feeds.txt");
        var feeds = await File.ReadAllLinesAsync(feedPath);
        var firstFeed = feeds.First(l => !string.IsNullOrWhiteSpace(l));

        // Go to Stations and Create
        await Page.ClickAsync("#profileDropdown");
        await Page.ClickAsync("text=\"Stations\"");
        await Page.ClickAsync("text=Create New Station");
        
        await Page.FillAsync("input[name='Name']", "Test Edit Station");
        await Page.FillAsync("input[name='Feeds']", firstFeed);
        await Page.ClickAsync("input[type='submit']");
        
        // Wait for redirect to Stations list
        await Page.WaitForURLAsync("**/Stations", new() { Timeout = 10000 });
        
        // Go to Home (Dashboard)
        await Page.GotoAsync(_factory.ServerAddress);

        // Wait for station to appear on dashboard
        var stationCard = Page.Locator(".card", new PageLocatorOptions { HasTextString = "Test Edit Station" }).First;
        await stationCard.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        
        // Wait for feeds to sync (not showing "0 podcasts")
        await Expect(stationCard.Locator("small.text-muted")).Not.ToContainTextAsync("0 podcasts", new() { Timeout = 30000 });
        
        // Click the edit gear icon
        await stationCard.Locator("a[href^='/Stations/Edit/']").ClickAsync();
        
        // Wait for Edit Station page
        await Expect(Page.Locator("h1")).ToContainTextAsync("Edit Station", new() { Timeout = 10000 });
        
        // Click the Resolved tab to see the playlist
        await Page.ClickAsync("#resolved-tab");

        // We should see the upcoming playlist section
        await Expect(Page.Locator("h4:has-text('Upcoming Playlist (Active Profile)')")).ToBeVisibleAsync();
        
        // Find the first episode in the list and click "Mark Completed"
        var initialCount = await Page.Locator(".resolved-item").CountAsync();
        var firstEpisode = Page.Locator(".resolved-item").First;
        var episodeTitle = await firstEpisode.Locator("strong").InnerTextAsync();
        
        Assert.That(episodeTitle, Is.Not.Empty);
        
        // Click Mark Completed
        Page.Dialog += (_, dialog) => dialog.AcceptAsync();
        await firstEpisode.Locator("button:has-text('Mark Completed')").ClickAsync();
        
        // Wait for count to decrease
        await Expect(Page.Locator(".resolved-item")).ToHaveCountAsync(initialCount - 1, new() { Timeout = 10000 });
    }

    [Test]
    public async Task Can_Add_And_Remove_Feed()
    {
        var url = _factory.ServerAddress;
        
        // Ensure profile exists
        var profileService = _factory.Services.GetRequiredService<Ohrbob.Web.Services.ProfilesApiService>();
        var profiles = await profileService.GetProfilesByAccountIdAsync(_testAccountId);
        if (profiles == null || !profiles.Any())
        {
            await profileService.CreateProfileAsync(_testAccountId, new Profile { Name = "Main Profile" });
        }
        
        Page.Console += (_, msg) => Console.WriteLine($"BROWSER CONSOLE: {msg.Text}");
        Page.PageError += (_, msg) => Console.WriteLine($"BROWSER ERROR: {msg}");
        await Page.GotoAsync(url);
        
        var feedPath = Path.Combine(Directory.GetCurrentDirectory(), "../../../../example_feeds.txt");
        if (!File.Exists(feedPath))
            feedPath = Path.Combine(Directory.GetCurrentDirectory(), "../../../../../example_feeds.txt");
        var feeds = await File.ReadAllLinesAsync(feedPath);
        var firstFeed = feeds.First(l => !string.IsNullOrWhiteSpace(l));
        var secondFeed = feeds.Skip(1).First(l => !string.IsNullOrWhiteSpace(l));

        // Go to Stations and Create
        await Page.ClickAsync("#profileDropdown");
        await Page.ClickAsync("text=\"Stations\"");
        await Page.ClickAsync("text=Create New Station");
        
        await Page.FillAsync("input[name='Name']", "Test Feed Station");
        await Page.FillAsync("input[name='Feeds']", firstFeed);
        await Page.ClickAsync("input[type='submit']");
        
        // Wait for redirect to Stations list
        await Page.WaitForURLAsync("**/Stations", new() { Timeout = 10000 });
        
        // Go to Home (Dashboard)
        await Page.GotoAsync(_factory.ServerAddress);

        // Wait for station to appear on dashboard
        var stationCard = Page.Locator(".card", new PageLocatorOptions { HasTextString = "Test Feed Station" }).First;
        try 
        {
            await stationCard.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        }
        catch
        {
            Console.WriteLine("DASHBOARD HTML: " + await Page.ContentAsync());
            throw;
        }
        
        // Wait for feeds to sync (not showing "0 podcasts")
        await Expect(stationCard.Locator("small.text-muted")).Not.ToContainTextAsync("0 podcasts", new() { Timeout = 30000 });
        
        // Click the edit gear icon
        await stationCard.Locator("a[href^='/Stations/Edit/']").ClickAsync();
        
        // Wait for Edit Station page
        await Expect(Page.Locator("h1")).ToContainTextAsync("Edit Station", new() { Timeout = 10000 });
        
        // Click Feeds tab
        await Page.ClickAsync("#feeds-tab");
        
        // Ensure there is 1 feed card initially
        var feedCards = Page.Locator(".feed-card");
        await Expect(feedCards).ToHaveCountAsync(1);
        
        // Add a new feed
        await Page.FillAsync("#newFeedUrl", secondFeed);
        await Page.ClickAsync("#addFeedBtn");
        
        // Check if there are 2 feed cards now
        await Expect(feedCards).ToHaveCountAsync(2, new() { Timeout = 15000 });
        
        // Now remove the feed
        Page.Dialog += (_, dialog) => dialog.AcceptAsync();
        
        var removeBtn = feedCards.Last.Locator(".remove-feed");
        await removeBtn.ClickAsync();
        
        // Check if there is 1 feed card
        await Expect(feedCards).ToHaveCountAsync(1, new() { Timeout = 10000 });
    }
}
