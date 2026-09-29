using Ohrbob.Api.Configuration;

namespace Ohrbob.Api;

public static class Consts
{
    public const string AllScope = "all";

    public static readonly IEnumerable<ApiPolicy> AllPolicies =
    [
        new ApiPolicy(GetHealthPolicy, AllScope),
        new ApiPolicy(GetScalarPolicy, AllScope),
        new ApiPolicy(CreateAccountPolicy, AllScope)
    ];
    
    public const string GetHealthPolicy = "get_health";
    public const string GetScalarPolicy = "get_scalar";

    public const string CreateAccountPolicy = "CreateAccount";

    
    
    public const string ScopeClaim = "scope";
    public const string SubjectClaim = "sub";
    
    public const string LogAuditScope = "AuditScope";
}