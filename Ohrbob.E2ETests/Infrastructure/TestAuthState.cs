namespace Ohrbob.E2ETests.Infrastructure;

/// <summary>
/// Per-host authentication state used by <see cref="TestAuthHandler"/>.
///
/// The suite runs test fixtures in parallel but the tests inside one fixture run
/// serially, and every fixture owns its own web host. Keeping the fake identity in
/// the host's DI container (instead of a process-global static) therefore lets
/// parallel fixtures impersonate different accounts without interfering.
/// </summary>
public sealed class TestAuthState
{
    /// <summary>Account id exposed as the <c>ohrbob_account_id</c> claim.</summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>When false, requests are treated as anonymous.</summary>
    public bool IsAuthenticated { get; set; } = true;
}
