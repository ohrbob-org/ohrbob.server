using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using MongoDB.Driver;
using NUnit.Framework;
using Ohrbob.E2ETests.Infrastructure;
using Ohrbob.Server.Shared.Data;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Ohrbob.E2ETests;

[NonParallelizable]
public class ProfilesTests : PageTest
{
    private PlaywrightWebApplicationFactory _factory = null!;
    private string _testAccountId = string.Empty;

    [SetUp]
    public async Task Setup()
    {
        await Context.ClearCookiesAsync();
        _factory = new PlaywrightWebApplicationFactory();
        
        var accountService = _factory.Services.GetRequiredService<Ohrbob.Web.Services.AccountApiService>();
        var uniqueEmail = $"profile-test-{Guid.NewGuid()}@test.com";
        var account = await accountService.CreateOrUpdateAccountAsync("E2E Profile Test", uniqueEmail, "", "Test", Guid.NewGuid().ToString());
        
        if (account != null) {
            // A Free account is limited to a single profile, which makes the multi-profile delete
            // flow unreachable. Grant Morebob so this test exercises the real delete path.
            await SetAccountTierAsync(account.Id, SubscriptionTier.Morebob);

            await accountService.CompleteOnboardingAsync(account.Id);
            var profileService = _factory.Services.GetRequiredService<Ohrbob.Web.Services.ProfilesApiService>();
            var profiles = await profileService.GetProfilesByAccountIdAsync(account.Id);
            if (profiles == null || !profiles.Any())
            {
                await profileService.CreateProfileAsync(account.Id, new Ohrbob.Server.Shared.Data.Profile { Name = "Main Profile" });
            }
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
    public async Task Cannot_Delete_Last_Profile()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Profiles");

        // Ensure exactly 1 profile card is shown
        var profileCards = Page.Locator(".card");
        await Expect(profileCards).ToHaveCountAsync(1);

        // Verify the Delete button is disabled
        var deleteBtn = Page.Locator("button:has-text('Delete')");
        await Expect(deleteBtn).ToBeDisabledAsync();

        // Create a second profile
        await Page.ClickAsync("text=Create New Profile");
        await Page.FillAsync("input[name='Name']", "Secondary Profile");
        await Page.ClickAsync("input[type='submit']");

        // We should be back on Profiles index and see 2 profile cards
        await Page.WaitForURLAsync("**/Profiles");
        await Expect(profileCards).ToHaveCountAsync(2);

        // Both Delete buttons should now be enabled
        var deleteBtns = Page.Locator("button:has-text('Delete')");
        await Expect(deleteBtns.First).ToBeEnabledAsync();
        await Expect(deleteBtns.Last).ToBeEnabledAsync();

        // Delete the non-active profile through the Bootstrap confirmation modal
        await deleteBtns.Last.ClickAsync();
        var confirmModal = Page.Locator("#confirmationModal");
        await Expect(confirmModal).ToBeVisibleAsync();
        await Page.ClickAsync("#confirmationModalConfirmBtn");

        // Verify we are back to 1 profile and Delete button is disabled again
        await Expect(profileCards).ToHaveCountAsync(1);
        await Expect(deleteBtn).ToBeDisabledAsync();
    }

    [Test]
    public async Task Profile_Color_IsSavedAndShownInCardAndNav()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Profiles");

        await Page.Locator("a:has-text('Edit')").First.ClickAsync();
        await Page.Locator(".profile-color-swatch[data-color='#3b82f6']").ClickAsync();
        await Page.Locator("input[type='submit']").ClickAsync();

        await Page.WaitForURLAsync("**/Profiles");

        await Expect(Page.Locator(".profile-avatar").First)
            .ToHaveCSSAsync("background-color", "rgb(59, 130, 246)");
        await Expect(Page.Locator("#profileDropdown div.rounded-circle").First)
            .ToHaveCSSAsync("background-color", "rgb(59, 130, 246)");
    }

    private static async Task SetAccountTierAsync(string accountId, SubscriptionTier tier)
    {
        // Mirrors the direct-Mongo seeding used by CompilingStationTests: the Api is a separate
        // process (Docker) whose tier config the in-process test host cannot influence, and the
        // SetTier endpoint is gated behind TIER_UPGRADE_ENABLED, so seed the account document.
        var client = new MongoClient("mongodb://localhost:27019");
        var accounts = client.GetDatabase("OhrbobData").GetCollection<Account>("AccountCollection");
        await accounts.UpdateOneAsync(a => a.Id == accountId, Builders<Account>.Update.Set(a => a.Tier, tier));
    }
}
