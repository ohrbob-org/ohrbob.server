using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;
using Ohrbob.E2ETests.Infrastructure;
using Ohrbob.Web.Services;

namespace Ohrbob.E2ETests;

public class StreamPageTests : E2EPageTest
{
    private AccountApiService _accountService = null!;
    private string _accountId = string.Empty;

    public override BrowserNewContextOptions ContextOptions() => new() { IgnoreHTTPSErrors = true };

    [SetUp]
    public async Task Setup()
    {
        SignIn();
        _accountService = _factory.Services.GetRequiredService<AccountApiService>();

        var account = await _accountService.CreateOrUpdateAccountAsync(
            "E2E Stream Test", $"stream-{Guid.NewGuid()}@test.com", "", "Test", Guid.NewGuid().ToString());
        _accountId = account!.Id;
        await _accountService.CompleteOnboardingAsync(_accountId);
        SetAccount(_accountId);
    }

    [TearDown]
    public async Task TearDown()
    {
        await Context.CloseAsync();
        SetAccount(string.Empty);
    }

    [Test]
    public async Task FreeAccount_SeesUpgradeNoticeAndNoStreamUrls()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Stream");

        await Expect(Page.Locator("text=Streaming is a Morebob feature")).ToBeVisibleAsync();
        await Expect(Page.Locator("code")).ToHaveCountAsync(0);
    }
}
