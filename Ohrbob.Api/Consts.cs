using Ohrbob.Api.Configuration;

namespace Ohrbob.Api;

public static class Consts
{
    public const string AllScope = "all";

    public static readonly IEnumerable<ApiPolicy> AllPolicies =
    [
        new ApiPolicy(GetScalarPolicy, AllScope),
        new ApiPolicy(CreateAccountPolicy, AllScope),
        new ApiPolicy(AddIdpPolicy, AllScope),
        new ApiPolicy(RemoveIdpPolicy, AllScope),
        new ApiPolicy(GetAccountPolicy, AllScope),
        new ApiPolicy(GetStationsPolicy, AllScope),
        new ApiPolicy(CreateStationPolicy, AllScope),
        new ApiPolicy(UpdateStationPolicy, AllScope),
        new ApiPolicy(DeleteStationPolicy, AllScope)
    ];
    
    public const string GetScalarPolicy = "get_scalar";

    public const string CreateAccountPolicy = nameof(CreateAccountPolicy);
    public const string AddIdpPolicy = nameof(AddIdpPolicy);
    public const string RemoveIdpPolicy = nameof(RemoveIdpPolicy);
    public const string GetAccountPolicy = nameof(GetAccountPolicy);
    public const string GetStationsPolicy = nameof(GetStationsPolicy);
    public const string CreateStationPolicy = nameof(CreateStationPolicy);
    public const string UpdateStationPolicy = nameof(UpdateStationPolicy);
    public const string DeleteStationPolicy = nameof(DeleteStationPolicy);
    
    
    
    public const string ScopeClaim = "scope";
    public const string SubjectClaim = "sub";
    
    public const string LogAuditScope = "AuditScope";
    
}