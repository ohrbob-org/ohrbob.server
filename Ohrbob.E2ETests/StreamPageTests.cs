using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;
using Ohrbob.E2ETests.Infrastructure;
using Ohrbob.Web.Services;

namespace Ohrbob.E2ETests;

[NonParallelizable]
public class StreamPageTests : PageTest
{
    private PlaywrightWebApplicationFactory _factory = null!;
    private AccountApiService _accountService = null!;
    private string _accountId = string.Empty;

    public override BrowserNewContextOptions ContextOptions() => new() { IgnoreHTTPSErrors = true };

    [SetUp]
    public async Task Setup()
    {
        TestAuthHandler.IsAuthenticated = true;
        _factory = new PlaywrightWebApplicationFactory();
        _accountService = _factory.Services.GetRequiredService<AccountApiService>();

        var account = await _accountService.CreateOrUpdateAccountAsync(
            "E2E Stream Test", $"stream-{Guid.NewGuid()}@test.com", "", "Test", Guid.NewGuid().ToString());
        _accountId = account!.Id;
        await _accountService.CompleteOnboardingAsync(_accountId);
        TestAuthHandler.TestAccountId = _accountId;
    }

    [TearDown]
    public async Task TearDown()
    {
        await Context.CloseAsync();
        _factory?.Dispose();
        TestAuthHandler.TestAccountId = string.Empty;
    }

    [Test]
    public async Task FreeAccount_SeesUpgradeNoticeAndNoStreamUrls()
    {
        await Page.GotoAsync($"{_factory.ServerAddress}/Stream");

        await Expect(Page.Locator("text=Streaming is a Morebob feature")).ToBeVisibleAsync();
        await Expect(Page.Locator("code")).ToHaveCountAsync(0);
    }
}
