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
            IgnoreHTTPSErrors = true,
            ViewportSize = new ViewportSize { Width = 375, Height = 812 }
        };
    }

    [SetUp]
    public async Task Setup()
    {
        await Context.ClearCookiesAsync();
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
    public async Task TearDown()
    {
        await Context.CloseAsync();
        _factory.Dispose();
        TestAuthHandler.TestAccountId = string.Empty;
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
        var stationCard = Page.Locator(".card-station", new PageLocatorOptions { HasTextString = "Test Edit Station" }).First;
        await stationCard.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        
        // Wait for feeds to sync (not showing "0 podcasts")
        await Expect(stationCard.Locator("small.text-muted")).Not.ToContainTextAsync("0 podcasts", new() { Timeout = 30000 });
        
        // Click the edit gear icon
        await stationCard.Locator("a[title='Manage Station']").ClickAsync();
        
        // Wait for Edit Station page
        await Expect(Page.Locator("h1")).ToContainTextAsync("Edit Station", new() { Timeout = 10000 });
        
        // Click the Resolved tab to see the playlist
        await Page.ClickAsync("#resolved-tab");

        // We should see the upcoming playlist section
        await Expect(Page.Locator("h4:has-text('Upcoming Playlist & Playhead')")).ToBeVisibleAsync();
        
        // Find the first episode in the list and click "Mark Completed"
        var firstEpisode = Page.Locator(".resolved-item").First;
        var episodeId = await firstEpisode.GetAttributeAsync("data-episode-id");
        var episodeTitle = await firstEpisode.Locator("strong").InnerTextAsync();
        
        Assert.That(episodeTitle, Is.Not.Empty);
        
        // Click Mark Completed Icon Button
        Page.Dialog += (_, dialog) => dialog.AcceptAsync();
        await firstEpisode.Locator("button.mark-completed-btn").ClickAsync();
        
        // Wait for completed overlay to be visible/active on the marked episode (using its unique ID since playhead advances and shifts list)
        var markedEpisode = Page.Locator($".resolved-item[data-episode-id='{episodeId}']");
        await Expect(markedEpisode.Locator(".completed-overlay")).ToBeVisibleAsync(new() { Timeout = 10000 });
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
        var stationCard = Page.Locator(".card-station", new PageLocatorOptions { HasTextString = "Test Feed Station" }).First;
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
        await stationCard.Locator("a[title='Manage Station']").ClickAsync();
        
        // Wait for Edit Station page
        await Expect(Page.Locator("h1")).ToContainTextAsync("Edit Station", new() { Timeout = 10000 });
        
        // Click Feeds tab
        await Page.ClickAsync("#rotation-tab");
        
        // Ensure there is 1 feed card initially
        var feedCards = Page.Locator(".rotation-item");
        await Expect(feedCards).ToHaveCountAsync(1);
        
        // Add a new feed (open the "More" menu, then the modal)
        await ClickStationMoreMenuItemAsync("#manualAddBtn");
        await Page.FillAsync("#newFeedUrl", secondFeed);
        await Page.ClickAsync("#submitManualAddBtn");
        
        // Check if there are 2 feed cards now
        await Expect(feedCards).ToHaveCountAsync(2, new() { Timeout = 15000 });
        
        // Now remove the feed via the three-dot menu (then the Bootstrap confirmation modal)
        await feedCards.Last.Locator(".feed-actions-menu").ClickAsync();
        await feedCards.Last.Locator(".remove-feed").ClickAsync();
        
        // Wait for confirmation modal to show and click confirm
        await Expect(Page.Locator("#confirmRemoveFeedModal")).ToBeVisibleAsync();
        await Page.ClickAsync("#confirmRemoveFeedBtn");
        
        // Check if there is 1 feed card
        await Expect(feedCards).ToHaveCountAsync(1, new() { Timeout = 10000 });
    }

    [Test]
    public async Task Can_Rename_Station_From_Edit_Page()
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
        
        await Page.FillAsync("input[name='Name']", "Original Station Name");
        await Page.FillAsync("input[name='Feeds']", firstFeed);
        await Page.ClickAsync("input[type='submit']");
        
        // Wait for redirect to Stations list
        await Page.WaitForURLAsync("**/Stations", new() { Timeout = 10000 });
        
        // Go to Home (Dashboard)
        await Page.GotoAsync(_factory.ServerAddress);

        // Wait for station to appear on dashboard
        var stationCard = Page.Locator(".card-station", new PageLocatorOptions { HasTextString = "Original Station Name" }).First;
        await stationCard.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        
        // Click the edit gear icon
        await stationCard.Locator("a[title='Manage Station']").ClickAsync();
        
        // Wait for Edit Station page
        await Expect(Page.Locator("h1")).ToContainTextAsync("Edit Station", new() { Timeout = 10000 });
        
        // Click the rename (pen) button
        await Page.ClickAsync("#renameStationBtn");
        
        // Modal should be visible
        await Expect(Page.Locator("#renameStationModal")).ToBeVisibleAsync();
        
        // Try filling empty name and click Save
        await Page.FillAsync("#newStationNameInput", "");
        await Page.ClickAsync("#saveStationNameBtn");
        
        // Error message should be shown (element lacks d-none class)
        await Expect(Page.Locator("#renameErrorMsg")).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("d-none"));
        
        // Fill valid new name and save
        await Page.FillAsync("#newStationNameInput", "Completely New Station Name");
        await Page.ClickAsync("#saveStationNameBtn");
        
        // Modal should close
        await Expect(Page.Locator("#renameStationModal")).Not.ToBeVisibleAsync();
        
        // Heading should be updated
        await Expect(Page.Locator("#stationNameDisplay")).ToHaveTextAsync("Completely New Station Name");
        
        // Document title should be updated
        var title = await Page.TitleAsync();
        Assert.That(title, Does.Contain("Completely New Station Name"));
    }

    [Test]
    public async Task Can_Delete_Station_From_Settings_Modal()
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
        
        await Page.FillAsync("input[name='Name']", "Station To Delete");
        await Page.FillAsync("input[name='Feeds']", firstFeed);
        await Page.ClickAsync("input[type='submit']");
        
        // Wait for redirect to Stations list
        await Page.WaitForURLAsync("**/Stations", new() { Timeout = 10000 });
        
        // Go to Home (Dashboard)
        await Page.GotoAsync(_factory.ServerAddress);

        // Wait for station to appear on dashboard
        var stationCard = Page.Locator(".card-station", new PageLocatorOptions { HasTextString = "Station To Delete" }).First;
        await stationCard.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        
        // Click the edit gear icon
        await stationCard.Locator("a[title='Manage Station']").ClickAsync();
        
        // Wait for Edit Station page
        await Expect(Page.Locator("h1")).ToContainTextAsync("Edit Station", new() { Timeout = 10000 });
        
        // Open the station settings modal
        await Page.ClickAsync("#renameStationBtn");
        await Expect(Page.Locator("#renameStationModal")).ToBeVisibleAsync();
        
        // Delete button should be visible on the left side of the modal footer
        await Expect(Page.Locator("#deleteStationBtn")).ToBeVisibleAsync();
        
        // Click delete: settings modal closes, confirmation modal opens
        await Page.ClickAsync("#deleteStationBtn");
        await Expect(Page.Locator("#renameStationModal")).Not.ToBeVisibleAsync();
        await Expect(Page.Locator("#confirmationModal")).ToBeVisibleAsync(new() { Timeout = 10000 });
        
        // Confirm deletion
        await Page.ClickAsync("#confirmationModalConfirmBtn");
        
        // Should be taken back to the stations list
        await Page.WaitForURLAsync("**/Stations", new() { Timeout = 15000 });
        await Expect(Page.Locator("h1")).ToContainTextAsync("My Stations", new() { Timeout = 10000 });
        
        // The deleted station should no longer be listed
        await Expect(Page.Locator(".card-station", new PageLocatorOptions { HasTextString = "Station To Delete" })).ToHaveCountAsync(0);
    }

    [Test]
    public async Task Can_Move_Feed_To_Another_Station()
    {
        var url = _factory.ServerAddress;

        Page.Console += (_, msg) => Console.WriteLine($"BROWSER CONSOLE: {msg.Text}");
        Page.PageError += (_, msg) => Console.WriteLine($"BROWSER ERROR: {msg}");

        var stationsService = _factory.Services.GetRequiredService<Ohrbob.Web.Services.StationsApiService>();

        var feedPath = Path.Combine(Directory.GetCurrentDirectory(), "../../../../example_feeds.txt");
        if (!File.Exists(feedPath))
            feedPath = Path.Combine(Directory.GetCurrentDirectory(), "../../../../../example_feeds.txt");
        var feeds = await File.ReadAllLinesAsync(feedPath);
        var feedA = feeds.First(l => !string.IsNullOrWhiteSpace(l));
        var feedB = feeds.Skip(1).First(l => !string.IsNullOrWhiteSpace(l));

        var stationA = await stationsService.CreateStationAsync(_testAccountId, new Station
        {
            AccountId = _testAccountId,
            Name = "Move Source",
            Feeds = new[] { feedA }
        });
        var stationB = await stationsService.CreateStationAsync(_testAccountId, new Station
        {
            AccountId = _testAccountId,
            Name = "Move Target",
            Feeds = new[] { feedB }
        });

        await Page.GotoAsync($"{url}/Stations/Edit/{stationA!.Id}");
        await Expect(Page.Locator("h1")).ToContainTextAsync("Edit Station", new() { Timeout = 10000 });

        // Set Span = 7 on the source feed and persist it
        var spanInput = Page.Locator("#rotationContainer .rotation-item .episodes-input").First;
        await spanInput.FillAsync("7");
        await Page.RunAndWaitForResponseAsync(
            async () => await spanInput.DispatchEventAsync("change"),
            r => r.Url.Contains("/Stations/UpdateSchedule/"));

        // Marker that survives only if there is no full page reload
        await Page.EvaluateAsync("() => { window.__moveMarker = 'kept'; }");

        // The three-dot menu is usable (i.e. not clipped by the card)
        var srcRow = Page.Locator("#rotationContainer .rotation-item").First;
        await Expect(srcRow.Locator(".feed-actions-menu")).ToBeVisibleAsync();
        await srcRow.Locator(".feed-actions-menu").ClickAsync();
        await Expect(srcRow.Locator(".move-feed")).ToBeVisibleAsync();

        // Regression (Review Focus #1): the menu must be painted where it is clicked,
        // not clipped by an ancestor's overflow. Playwright's locator.click() ignores
        // ancestor clipping, so hit-test the real point.
        var deleteItem = srcRow.Locator(".remove-feed");
        var deleteBox = await deleteItem.BoundingBoxAsync();
        Assert.That(deleteBox, Is.Not.Null, "The 'Delete feed' item has no bounding box");
        var deleteHit = await Page.EvaluateAsync<bool>(
            "([x, y]) => { const el = document.elementFromPoint(x, y); return !!el && el.closest('.remove-feed') !== null; }",
            new[] { deleteBox!.X + deleteBox.Width / 2.0, deleteBox.Y + deleteBox.Height / 2.0 });
        Assert.That(deleteHit, Is.True, "The 'Delete feed' menu item is not hit-testable at its centre (clipped by the card)");

        await srcRow.Locator(".move-feed").ClickAsync();

        // Modal offers the other station (not the source) and create-new
        await Expect(Page.Locator("#moveFeedModal")).ToBeVisibleAsync();
        await Expect(Page.Locator($"#moveTarget-{stationB.Id}")).ToBeVisibleAsync();
        await Expect(Page.Locator($"#moveTarget-{stationA.Id}")).ToHaveCountAsync(0);
        await Expect(Page.Locator("#moveTargetNew")).ToBeVisibleAsync();

        // Perform the move
        await Page.Locator($"#moveTarget-{stationB.Id}").CheckAsync();
        await Page.ClickAsync("#confirmMoveFeedBtn");

        await Expect(Page.Locator("#toast-body-text")).ToContainTextAsync("Moved", new() { Timeout = 10000 });
        await Expect(Page.Locator("#rotationContainer .rotation-item")).ToHaveCountAsync(0, new() { Timeout = 10000 });

        // No navigation / no full page reload (playback must be untouched)
        Assert.That(Page.Url, Does.Contain("/Stations/Edit/"));
        Assert.That(await Page.EvaluateAsync<string>("() => window.__moveMarker || ''"), Is.EqualTo("kept"));
        Assert.That(await Page.EvaluateAsync<bool>("() => !!document.querySelector('audio-widget')"), Is.True,
            "The audio widget must survive the move (playback must not be interrupted)");

        // Destination now has the moved feed, with the carried Span
        await Page.GotoAsync($"{url}/Stations/Edit/{stationB.Id}");
        var movedRow = Page.Locator($"#rotationContainer .rotation-item[data-url=\"{feedA}\"]");
        await Expect(movedRow).ToHaveCountAsync(1, new() { Timeout = 10000 });
        Assert.That(await movedRow.Locator(".episodes-input").InputValueAsync(), Is.EqualTo("7"));
    }

    [Test]
    public async Task MoveModal_OffersOnlyCreateNew_WhenNoOtherStation()
    {
        var url = _factory.ServerAddress;
        var stationsService = _factory.Services.GetRequiredService<Ohrbob.Web.Services.StationsApiService>();

        var feedPath = Path.Combine(Directory.GetCurrentDirectory(), "../../../../example_feeds.txt");
        if (!File.Exists(feedPath))
            feedPath = Path.Combine(Directory.GetCurrentDirectory(), "../../../../../example_feeds.txt");
        var feeds = await File.ReadAllLinesAsync(feedPath);
        var feed = feeds.First(l => !string.IsNullOrWhiteSpace(l));

        var onlyStation = await stationsService.CreateStationAsync(_testAccountId, new Station
        {
            AccountId = _testAccountId,
            Name = "Only Station",
            Feeds = new[] { feed }
        });

        await Page.GotoAsync($"{url}/Stations/Edit/{onlyStation!.Id}");
        await Expect(Page.Locator("h1")).ToContainTextAsync("Edit Station", new() { Timeout = 10000 });

        var row = Page.Locator("#rotationContainer .rotation-item").First;
        await Expect(row).ToBeVisibleAsync(new() { Timeout = 10000 });
        await row.Locator(".feed-actions-menu").ClickAsync();
        await row.Locator(".move-feed").ClickAsync();

        await Expect(Page.Locator("#moveFeedModal")).ToBeVisibleAsync();
        await Expect(Page.Locator("#moveTargetNew")).ToBeVisibleAsync();
        // Only the "__new__" radio exists — no other station to pick
        await Expect(Page.Locator("#moveFeedStationList input[name='moveTarget']")).ToHaveCountAsync(1);
    }

    [Test]
    public async Task Can_Move_Feed_To_A_New_Station_And_It_AppearsInTheDestinationList()
    {
        var url = _factory.ServerAddress;
        var stationsService = _factory.Services.GetRequiredService<Ohrbob.Web.Services.StationsApiService>();

        var feedPath = Path.Combine(Directory.GetCurrentDirectory(), "../../../../example_feeds.txt");
        if (!File.Exists(feedPath))
            feedPath = Path.Combine(Directory.GetCurrentDirectory(), "../../../../../example_feeds.txt");
        var feeds = await File.ReadAllLinesAsync(feedPath);
        var feed1 = feeds.First(l => !string.IsNullOrWhiteSpace(l));
        var feed2 = feeds.Skip(1).First(l => !string.IsNullOrWhiteSpace(l));

        var station = await stationsService.CreateStationAsync(_testAccountId, new Station
        {
            AccountId = _testAccountId,
            Name = "Origin Station",
            Feeds = new[] { feed1, feed2 }
        });

        await Page.GotoAsync($"{url}/Stations/Edit/{station!.Id}");
        await Expect(Page.Locator("h1")).ToContainTextAsync("Edit Station", new() { Timeout = 10000 });

        // Move feed1 into a brand-new station
        var row1 = Page.Locator($"#rotationContainer .rotation-item[data-url=\"{feed1}\"]");
        await Expect(row1).ToHaveCountAsync(1, new() { Timeout = 10000 });
        await row1.Locator(".feed-actions-menu").ClickAsync();
        await row1.Locator(".move-feed").ClickAsync();
        await Expect(Page.Locator("#moveFeedModal")).ToBeVisibleAsync();
        await Page.Locator("#moveTargetNew").CheckAsync();
        await Page.FillAsync("#moveNewStationName", "Brand New Station");
        await Page.ClickAsync("#confirmMoveFeedBtn");

        await Expect(Page.Locator("#toast-body-text")).ToContainTextAsync("Brand New Station", new() { Timeout = 10000 });
        await Expect(Page.Locator($"#rotationContainer .rotation-item[data-url=\"{feed1}\"]")).ToHaveCountAsync(0, new() { Timeout = 10000 });

        // The modal's destination list must refresh to include the new station (no reload)
        var row2 = Page.Locator($"#rotationContainer .rotation-item[data-url=\"{feed2}\"]");
        await row2.Locator(".feed-actions-menu").ClickAsync();
        await row2.Locator(".move-feed").ClickAsync();
        await Expect(Page.Locator("#moveFeedStationList")).ToContainTextAsync("Brand New Station", new() { Timeout = 10000 });
    }

    [Test]
    public async Task Can_Import_Feeds_From_An_Opml_File()
    {
        var url = _factory.ServerAddress;
        var stationsService = _factory.Services.GetRequiredService<Ohrbob.Web.Services.StationsApiService>();

        var feedPath = Path.Combine(Directory.GetCurrentDirectory(), "../../../../example_feeds.txt");
        if (!File.Exists(feedPath))
            feedPath = Path.Combine(Directory.GetCurrentDirectory(), "../../../../../example_feeds.txt");
        var feeds = await File.ReadAllLinesAsync(feedPath);
        var existingFeed = feeds.First(l => !string.IsNullOrWhiteSpace(l));
        var newFeed = feeds.Skip(1).First(l => !string.IsNullOrWhiteSpace(l));

        var station = await stationsService.CreateStationAsync(_testAccountId, new Station
        {
            AccountId = _testAccountId,
            Name = "OPML Import Station",
            Feeds = new[] { existingFeed }
        });

        // The OPML contains one feed the station already has and one new feed.
        var opmlPath = Path.Combine(Path.GetTempPath(), $"ohrbob-subs-{Guid.NewGuid():N}.opml");
        await File.WriteAllTextAsync(opmlPath, $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<opml version=""2.0"">
  <head><title>Test Subscriptions</title></head>
  <body>
    <outline text=""Existing"" type=""rss"" xmlUrl=""{existingFeed}"" />
    <outline text=""New"" type=""rss"" xmlUrl=""{newFeed}"" />
  </body>
</opml>");

        try
        {
            await Page.GotoAsync($"{url}/Stations/Edit/{station!.Id}");
            await Expect(Page.Locator("h1")).ToContainTextAsync("Edit Station", new() { Timeout = 10000 });
            await Expect(Page.Locator("#rotationContainer .rotation-item")).ToHaveCountAsync(1, new() { Timeout = 10000 });

            await ClickStationMoreMenuItemAsync("#importOpmlBtn");
            await Expect(Page.Locator("#importOpmlModal")).ToBeVisibleAsync();
            await Page.SetInputFilesAsync("#opmlFileInput", opmlPath);
            await Page.ClickAsync("#submitOpmlBtn");

            await Expect(Page.Locator("#toast-body-text")).ToContainTextAsync("Imported 1", new() { Timeout = 60000 });
            await Expect(Page.Locator("#rotationContainer .rotation-item")).ToHaveCountAsync(2, new() { Timeout = 60000 });

            // The modal closed and we stayed on the edit page (playback untouched).
            await Expect(Page.Locator("#importOpmlModal")).Not.ToBeVisibleAsync();
            Assert.That(Page.Url, Does.Contain("/Stations/Edit/"));
        }
        finally
        {
            File.Delete(opmlPath);
        }
    }

    [Test]
    public async Task ImportOpml_ShowsA_Toast_When_The_File_Is_Not_An_Opml()
    {
        var url = _factory.ServerAddress;
        var stationsService = _factory.Services.GetRequiredService<Ohrbob.Web.Services.StationsApiService>();

        var station = await stationsService.CreateStationAsync(_testAccountId, new Station
        {
            AccountId = _testAccountId,
            Name = "Invalid OPML Station",
            Feeds = Array.Empty<string>()
        });

        var badFile = Path.Combine(Path.GetTempPath(), $"ohrbob-bad-{Guid.NewGuid():N}.opml");
        await File.WriteAllTextAsync(badFile, "<html><body>this is not an opml file</body></html>");

        try
        {
            await Page.GotoAsync($"{url}/Stations/Edit/{station!.Id}");
            await Expect(Page.Locator("h1")).ToContainTextAsync("Edit Station", new() { Timeout = 10000 });

            await ClickStationMoreMenuItemAsync("#importOpmlBtn");
            await Expect(Page.Locator("#importOpmlModal")).ToBeVisibleAsync();
            await Page.SetInputFilesAsync("#opmlFileInput", badFile);
            await Page.ClickAsync("#submitOpmlBtn");

            await Expect(Page.Locator("#toast-body-text")).ToContainTextAsync("not a valid OPML", new() { Timeout = 15000 });
            // The modal stays open so the user can pick another file.
            await Expect(Page.Locator("#importOpmlModal")).ToBeVisibleAsync();
            await Expect(Page.Locator("#rotationContainer .rotation-item")).ToHaveCountAsync(0);
        }
        finally
        {
            File.Delete(badFile);
        }
    }

    [Test]
    public async Task Feed_Url_And_Import_Opml_Live_In_The_More_Menu()
    {
        var url = _factory.ServerAddress;
        var stationsService = _factory.Services.GetRequiredService<Ohrbob.Web.Services.StationsApiService>();

        var station = await stationsService.CreateStationAsync(_testAccountId, new Station
        {
            AccountId = _testAccountId,
            Name = "More Menu Station",
            Feeds = Array.Empty<string>()
        });

        await Page.GotoAsync($"{url}/Stations/Edit/{station!.Id}");
        await Expect(Page.Locator("h1")).ToContainTextAsync("Edit Station", new() { Timeout = 10000 });

        // Discover stays a standalone primary action.
        await Expect(Page.Locator("a.btn[href='/Discover']")).ToBeVisibleAsync();

        // Feed Url and Import OPML are menu items now, not standalone buttons.
        await Expect(Page.Locator("#stationMoreMenu #manualAddBtn.dropdown-item")).ToHaveCountAsync(1);
        await Expect(Page.Locator("#stationMoreMenu #importOpmlBtn.dropdown-item")).ToHaveCountAsync(1);
        await Expect(Page.Locator("button#manualAddBtn.btn")).ToHaveCountAsync(0);
        await Expect(Page.Locator("button#importOpmlBtn.btn")).ToHaveCountAsync(0);

        // The menu is collapsed until the "More" button is clicked.
        await Expect(Page.Locator("#stationMoreMenuBtn")).ToContainTextAsync("More");
        await Expect(Page.Locator("#manualAddBtn")).ToBeHiddenAsync();
        await Expect(Page.Locator("#importOpmlBtn")).ToBeHiddenAsync();

        await Page.ClickAsync("#stationMoreMenuBtn");
        await Expect(Page.Locator("#manualAddBtn")).ToBeVisibleAsync();
        await Expect(Page.Locator("#importOpmlBtn")).ToBeVisibleAsync();

        // Feed Url opens the manual add modal.
        await Page.ClickAsync("#manualAddBtn");
        await Expect(Page.Locator("#manualAddFeedModal")).ToBeVisibleAsync();
        await Page.ClickAsync("#manualAddFeedModal .btn-secondary");
        await Expect(Page.Locator("#manualAddFeedModal")).Not.ToBeVisibleAsync();

        // Import OPML opens the OPML modal.
        await Page.ClickAsync("#stationMoreMenuBtn");
        await Page.ClickAsync("#importOpmlBtn");
        await Expect(Page.Locator("#importOpmlModal")).ToBeVisibleAsync();
    }

    [Test]
    public async Task EndlessSpin_Toggle_PersistsAcrossReload()
    {
        var stationsService = _factory.Services.GetRequiredService<Ohrbob.Web.Services.StationsApiService>();
        var station = await stationsService.CreateStationAsync(_testAccountId, new Station
        {
            AccountId = _testAccountId,
            Name = "Endless Spin Station",
            Feeds = Array.Empty<string>()
        });

        await Page.GotoAsync($"{_factory.ServerAddress}/Stations/Edit/{station!.Id}");
        await Expect(Page.Locator("h1")).ToContainTextAsync("Edit Station", new() { Timeout = 10000 });

        await Page.ClickAsync("#renameStationBtn");
        await Expect(Page.Locator("#endlessSpinInput")).Not.ToBeCheckedAsync();

        await Page.CheckAsync("#endlessSpinInput");
        await Page.ClickAsync("#saveStationNameBtn");
        await Expect(Page.Locator("#renameStationModal")).Not.ToBeVisibleAsync(new() { Timeout = 10000 });

        await Page.ReloadAsync();
        await Page.ClickAsync("#renameStationBtn");
        await Expect(Page.Locator("#endlessSpinInput")).ToBeCheckedAsync();
    }

    private async Task ClickStationMoreMenuItemAsync(string itemSelector)
    {
        await Page.ClickAsync("#stationMoreMenuBtn");
        await Expect(Page.Locator(itemSelector)).ToBeVisibleAsync();
        await Page.ClickAsync(itemSelector);
    }
}
