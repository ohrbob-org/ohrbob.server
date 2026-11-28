using Microsoft.Playwright.NUnit;
using Ohrbob.E2ETests.Infrastructure;

namespace Ohrbob.E2ETests;

/// <summary>
/// Starts one in-process web host per test class instead of one per test.
/// Tests inside a class run serially and share the fixture's host, so the fake
/// identity lives in that host's DI container and parallel fixtures cannot
/// impersonate each other's accounts.
/// </summary>
public abstract class E2EPageTest : PageTest
{
    // Program startup resolves the app's host through shared static state in
    // HostFactoryResolver. Starting several fixtures at the very same moment can
    // make one of them come up without a server, so host creation is serialized.
    // The tests themselves still run in parallel; this lock only covers startup.
    private static readonly object HostStartupLock = new();

    protected PlaywrightWebApplicationFactory _factory = null!;

    private TestAuthState? _auth;

    /// <summary>The fake identity of this fixture's host.</summary>
    protected TestAuthState Auth => _auth ??= _factory.AuthState;

    /// <summary>Marks subsequent requests as authenticated (keeping the current account).</summary>
    protected void SignIn() => Auth.IsAuthenticated = true;

    /// <summary>Marks subsequent requests as anonymous.</summary>
    protected void SignOut() => Auth.IsAuthenticated = false;

    /// <summary>Switches the account impersonated by subsequent requests.</summary>
    protected void SetAccount(string accountId) => Auth.AccountId = accountId;

    [OneTimeSetUp]
    public void StartSharedHost()
    {
        _factory = new PlaywrightWebApplicationFactory();
        lock (HostStartupLock)
        {
            _factory.CreateClient();
        }
    }

    [OneTimeTearDown]
    public void StopSharedHost()
    {
        _factory?.Dispose();
    }
}
