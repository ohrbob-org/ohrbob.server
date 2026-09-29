namespace Ohrbob.Api;

public class Consts
{
    public const string GetHealthPolicy = "get_health";
    public const string GetScalarPolicy = "get_scalar";

    public const string AllScope = "all";
    public const string CreateAccountPolicy = "CreateAccount";

    public static readonly IEnumerable<string> AllPolicies =
    [
        GetHealthPolicy,
        GetScalarPolicy,
        CreateAccountPolicy
    ];
    
    public const string ScopeClaim = "scope";
    public const string SubjectClaim = "sub";
}