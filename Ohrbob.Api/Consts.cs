namespace Ohrbob.Api;

public class Consts
{
    public const string GetHealthScope = "get_health";
    public const string GetScalarScope = "get_scalar";

    public const string AllScope = "all";
    public const string AccountRegisterScope = "account_register";
    public const string AccountListUsersScope = "account_list_users";
    public const string AccountCreateUsersScope = "account_create_users";
    public const string AccountGetScopesScope = "account_get_scopes";
    
    public static readonly IEnumerable<string> AllScopes = [
    GetHealthScope,
    GetScalarScope,
    
    AccountRegisterScope,
    AccountListUsersScope,
    AccountCreateUsersScope,
    AccountGetScopesScope
    ];
    public static readonly IEnumerable<string> AllGetScopes = [GetHealthScope,GetScalarScope,
        AccountGetScopesScope];

    // Scopes granted to a newly registered account. account_register, get_health and get_scalar stay
    // API-key only: the first provisions accounts, the other two are infrastructure endpoints.
    public static readonly string[] DefaultAccountScopes =
    [
        AccountListUsersScope,
        AccountCreateUsersScope,
        AccountGetScopesScope
    ];
    
    public const string ScopeClaim = "scope";
    public const string SubjectClaim = "sub";
}