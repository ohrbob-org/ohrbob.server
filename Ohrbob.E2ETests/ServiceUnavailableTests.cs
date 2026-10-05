using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;
using Ohrbob.E2ETests.Infrastructure;

namespace Ohrbob.E2ETests;

public class DeadBackendFactory : PlaywrightWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        
        // Override the configuration to point to a guaranteed dead port
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "OhrbobApi:BaseUrl", "http://127.0.0.1:59999/api" }
            });
        });
    }
}

public class ServiceUnavailableTests : PageTest
{
    private DeadBackendFactory _factory = null!;

    [SetUp]
    public void Setup()
    {
        _factory = new DeadBackendFactory();
        // Accessing the ServerAddress explicitly causes the lazy WebApplicationFactory to instantiate the host.
        // We use CreateClient() to force the factory to build the internal host.
        var _ = _factory.CreateClient();
        
        TestAuthHandler.TestAccountId = "test-account-id"; // Provide a dummy auth to trigger backend calls
    }

    [TearDown]
    public void TearDown()
    {
        _factory.Dispose();
    }

    public override BrowserNewContextOptions ContextOptions()
    {
        return new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true
        };
    }

    [Test]
    public async Task WhenBackendIsDown_RedirectsToServiceUnavailable_WithoutLoop()
    {
        // Act: Navigate to the Home page.
        // Because the user is "authenticated" (via TestAuthHandler), the HomeController 
        // will attempt to hit the backend API to fetch profiles and stations.
        // Since we configured the factory to point to a dead port, it will throw a SocketException.
        // The global error handler should catch it and redirect to /Home/ServiceUnavailable.
        
        // If there is an infinite redirect loop (ERR_TOO_MANY_REDIRECTS), Playwright will throw an exception here.
        await Page.GotoAsync(_factory.ServerAddress);

        // Assert: We should end up on the Service Unavailable page, and it should render successfully.
        await Expect(Page.Locator("h1")).ToContainTextAsync("Service Unavailable");
        
        // Verify the URL path actually changed to the expected route
        Assert.That(new Uri(Page.Url).AbsolutePath, Is.EqualTo("/Home/ServiceUnavailable"));
    }

    [Test]
    public async Task DirectNavigationToServiceUnavailable_RendersSuccessfully()
    {
        // Act: Go directly to the endpoint to ensure it renders on its own without looping
        await Page.GotoAsync($"{_factory.ServerAddress}/Home/ServiceUnavailable");

        // Assert
        await Expect(Page.Locator("h1")).ToContainTextAsync("Service Unavailable");
    }
}