using System.Security.Claims;

namespace Ohrbob.Api.Extensions;

public class ConfigApiKey
{
    public string Key { get; set; } = "";
    public string[] Scopes { get; set; } =  Array.Empty<string>();
    public string Name { get; set; } = "";

    public List<Claim> GetClaims()
    {
        var claims = new List<Claim>();
        foreach (var scope in Scopes)
            claims.Add(new Claim(Consts.ScopeClaim, scope));
        return claims;
    }
}