using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ohrbob.E2ETests.Infrastructure;

public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>
    /// Process-wide fallback used by hosts that do not register a
    /// <see cref="TestAuthState"/> (e.g. ad-hoc factories). Production E2E fixtures
    /// resolve the state from their own host instead, which keeps parallel
    /// fixtures isolated from each other.
    /// </summary>
    public static string TestAccountId { get; set; } = string.Empty;
    public static bool IsAuthenticated { get; set; } = true;

    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var state = Context.RequestServices.GetService<TestAuthState>();
        var isAuthenticated = state?.IsAuthenticated ?? IsAuthenticated;
        var accountId = state?.AccountId ?? TestAccountId;

        if (!isAuthenticated)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        Console.WriteLine($"[TestAuthHandler] Authenticating request with TestAccountId: {accountId}");

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "test-user-id"),
            new Claim(ClaimTypes.Name, "E2E Test User"),
            new Claim("ohrbob_account_id", accountId)
        };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
