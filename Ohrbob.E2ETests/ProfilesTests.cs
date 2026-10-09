using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;
using Ohrbob.E2ETests.Infrastructure;
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

        // Intercept native dialog confirmation and click Delete on the non-active profile
        Page.Dialog += (_, dialog) => dialog.AcceptAsync();
        await deleteBtns.Last.ClickAsync();

        // Verify we are back to 1 profile and Delete button is disabled again
        await Page.WaitForURLAsync("**/Profiles");
        await Expect(profileCards).ToHaveCountAsync(1);
        await Expect(deleteBtn).ToBeDisabledAsync();
    }
}
