using System.Security.Claims;
using AspNetCore.Authentication.ApiKey;

namespace Ohrbob.Api.Extensions;

public class ApiKey : IApiKey
{
    public ApiKey(string key, string name, List<Claim> claims)
    {
        Key = key;
        OwnerName = name;
        Claims = claims;
    }

    public string Key { get; }
    public string OwnerName { get; }
    public IReadOnlyCollection<Claim> Claims { get; }
}