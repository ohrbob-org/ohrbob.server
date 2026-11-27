using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;
using Ohrbob.E2ETests.Infrastructure;
using Ohrbob.Web.Services;

namespace Ohrbob.E2ETests;

public class AccountDeletionTests : E2EPageTest
{
    private string _accountId = string.Empty;

    public override BrowserNewContextOptions ContextOptions() => new() { IgnoreHTTPSErrors = true };

    [SetUp]
    public async Task Setup()
    {
        await Context.ClearCookiesAsync();
        SignIn();

        Page.Console += (_, msg) => Console.WriteLine($"BROWSER CONSOLE: {msg.Text}");
        Page.PageError += (_, msg) => Console.WriteLine($"BROWSER ERROR: {msg}");

        var accountService = _factory.Services.GetRequiredService<AccountApiService>();
        var account = await accountService.CreateOrUpdateAccountAsync(
            "Delete Me", $"delete-{Guid.NewGuid()}@test.com", "", "Test", Guid.NewGuid().ToString());
        await accountService.CompleteOnboardingAsync(account!.Id);
        _accountId = account.Id;

        SetAccount(_accountId);
        _factory.CreateClient();
    }

    [TearDown]
    public async Task TearDown()
    {
        await Context.CloseAsync();
        SetAccount(string.Empty);
        SignIn();
    }

    [Test]
    public async Task DeleteAccount_FromConfirmationPage_RemovesAccountAndSignsOut()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Account");
        await Expect(Page.Locator("h1")).ToHaveTextAsync("account");

        await Page.ClickAsync("#delete-tab");
        await Expect(Page.Locator("#delete-pane")).ToBeVisibleAsync();

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
