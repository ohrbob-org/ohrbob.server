using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;
using Ohrbob.E2ETests.Infrastructure;

namespace Ohrbob.E2ETests;

public class AlreadyRegisteredPageTests : E2EPageTest
{

    [SetUp]
    public void Setup()
    {
        SignOut();
    }

    [TearDown]
    public void TearDown()
    {
        SignIn();
    }

    [Test]
    public async Task AlreadyRegisteredPage_ExplainsIdentityProviderConflict()
    {
        _factory.CreateClient();
        await Page.GotoAsync($"{_factory.ServerAddress}/Home/AlreadyRegistered");

        await Expect(Page.Locator("h1")).ToContainTextAsync("Already registered");
        await Expect(Page.Locator("body")).ToContainTextAsync("already registered with another identity provider");
    }
}
