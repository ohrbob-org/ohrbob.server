using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using Ohrbob.E2ETests.Infrastructure;
using Ohrbob.Web.Services;

namespace Ohrbob.E2ETests;

[NonParallelizable]
public class AccountBillingTests : PageTest
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
            "Billing User", $"billing-{Guid.NewGuid()}@test.com", "", "Test", Guid.NewGuid().ToString());
        await accountService.CompleteOnboardingAsync(account!.Id);

        TestAuthHandler.TestAccountId = account.Id;
        _accountId = account.Id;
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
    public async Task AccountPage_HasTabsInOrder()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Account");

        var tabs = Page.Locator("#accountTabsNav .account-tab-link");
        await Expect(tabs).ToHaveCountAsync(5);
        await Expect(tabs.Nth(0)).ToContainTextAsync("Account & Billing");
        await Expect(tabs.Nth(1)).ToContainTextAsync("Appearance");
        await Expect(tabs.Nth(2)).ToContainTextAsync("API Keys");
        await Expect(tabs.Nth(3)).ToContainTextAsync("Report a problem");
        await Expect(tabs.Nth(4)).ToContainTextAsync("Delete Account");
        await Expect(Page.Locator("#accountTabsContent")).ToBeHiddenAsync();
    }

    [Test]
    public async Task ReportProblem_ShowsAccountEmailAndGitHubIssueLinks()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Account");
        await Page.ClickAsync("#report-tab");

        await Expect(Page.Locator("#report-pane")).ToBeVisibleAsync();
        await Expect(Page.Locator("#report-pane")).ToContainTextAsync("Problem with my account or billing");
        await Expect(Page.Locator("#report-pane a[href^='mailto:support@example.test']")).ToBeVisibleAsync();
        await Expect(Page.Locator("#report-pane a[href='https://github.com/ohrbob-org/ohrbob.server/issues/']")).ToBeVisibleAsync();
    }

    [Test]
    public async Task AccountTabs_OnNarrowScreen_ShowListThenDetailsWithBack()
    {
        await Page.SetViewportSizeAsync(375, 800);
        await Page.GotoAsync($"{_factory.ServerAddress}/Account");

        await Expect(Page.Locator("#accountTabsNav")).ToBeVisibleAsync();
        await Expect(Page.Locator("#accountTabsContent")).ToBeHiddenAsync();

        await Page.ClickAsync("#appearance-tab");

        await Expect(Page.Locator("#accountTabsNav")).ToBeHiddenAsync();
        await Expect(Page.Locator("#accountTabsContent")).ToBeVisibleAsync();
        await Expect(Page.Locator("#accountTabsBack")).ToBeVisibleAsync();

        await Page.ClickAsync("#accountTabsBack");

        await Expect(Page.Locator("#accountTabsNav")).ToBeVisibleAsync();
        await Expect(Page.Locator("#accountTabsContent")).ToBeHiddenAsync();
    }

    [Test]
    public async Task FreeAccount_ShowsStripeUpgradeInsteadOfComingSoonBanner()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Account");
        await Page.ClickAsync("#account-tab");

        await Expect(Page.Locator("h1")).ToContainTextAsync("Manage Account");
        await Expect(Page.Locator("form[action='/Account/Upgrade'] button")).ToContainTextAsync("Upgrade to Morebob");
        await Expect(Page.Locator("text=Stripe Card Billing Integration Coming Soon")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task MorebobAccount_ShowsRenewalAndCancellationDeadline()
    {
        var accounts = new MongoClient("mongodb://localhost:27019")
            .GetDatabase("OhrbobData")
            .GetCollection<BsonDocument>("AccountCollection");
        var account = Builders<BsonDocument>.Filter.Eq("_id", _accountId);
        var periodEnd = new DateTime(2026, 12, 9, 10, 57, 25, DateTimeKind.Utc);

        await accounts.UpdateOneAsync(account, Builders<BsonDocument>.Update
            .Set("Tier", 1)
            .Set("StripeCustomerId", "cus_e2e")
            .Set("StripeSubscriptionStatus", "active")
            .Set("StripeCurrentPeriodEnd", periodEnd)
            .Set("StripeCancelAt", BsonNull.Value));

        await Page.GotoAsync($"{_factory.ServerAddress}/Account");
        await Page.ClickAsync("#account-tab");
        await Expect(Page.Locator("text=Renews on 9 Dec 2026")).ToBeVisibleAsync();
        Assert.That(await Page.Locator("#morebob-thanks").TextContentAsync(), Is.Not.Null.And.Not.Empty);

        await accounts.UpdateOneAsync(account, Builders<BsonDocument>.Update
            .Set("StripeCancelAt", periodEnd));

        await Page.ReloadAsync();
        await Page.ClickAsync("#account-tab");
        await Expect(Page.Locator("text=Renewal is off.")).ToBeVisibleAsync();
        await Expect(Page.Locator("text=ends on 9 Dec 2026")).ToBeVisibleAsync();
    }
}
