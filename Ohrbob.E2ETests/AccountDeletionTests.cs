using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;
using Ohrbob.E2ETests.Infrastructure;
using Ohrbob.Web.Services;

namespace Ohrbob.E2ETests;

[NonParallelizable]
public class AccountDeletionTests : PageTest
{
    private PlaywrightWebApplicationFactory _factory = null!;
    private string _accountId = string.Empty;

    public override BrowserNewContextOptions ContextOptions() => new() { IgnoreHTTPSErrors = true };

    [SetUp]
    public async Task Setup()
    {
        await Context.ClearCookiesAsync();
        TestAuthHandler.IsAuthenticated = true;

        _factory = new PlaywrightWebApplicationFactory();
        Page.Console += (_, msg) => Console.WriteLine($"BROWSER CONSOLE: {msg.Text}");
        Page.PageError += (_, msg) => Console.WriteLine($"BROWSER ERROR: {msg}");

        var accountService = _factory.Services.GetRequiredService<AccountApiService>();
        var account = await accountService.CreateOrUpdateAccountAsync(
            "Delete Me", $"delete-{Guid.NewGuid()}@test.com", "", "Test", Guid.NewGuid().ToString());
        await accountService.CompleteOnboardingAsync(account!.Id);
        _accountId = account.Id;

        TestAuthHandler.TestAccountId = _accountId;
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
    public async Task DeleteAccount_FromConfirmationPage_RemovesAccountAndSignsOut()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Account");
        await Expect(Page.Locator("h1")).ToContainTextAsync("Manage Account");

        var deleteLink = Page.Locator("a[href='/Account/Delete']");
        await deleteLink.ScrollIntoViewIfNeededAsync();
        await Expect(deleteLink).ToBeVisibleAsync();
        await deleteLink.ClickAsync();

        await Page.WaitForURLAsync(new Regex("/Account/Delete$"), new() { Timeout = 15000 });
        await Expect(Page.Locator("h1")).ToContainTextAsync("Delete account");
        await Expect(Page.Locator("text=This cannot be undone.")).ToBeVisibleAsync();

        await Page.ClickAsync("button[type='submit']:has-text('Delete my account')");

        await Page.WaitForURLAsync($"{_factory.ServerAddress}/", new() { Timeout = 15000 });

        var accountService = _factory.Services.GetRequiredService<AccountApiService>();
        Assert.That(await accountService.GetAccountAsync(_accountId), Is.Null,
            "The account must be gone after confirming deletion");
    }
}
