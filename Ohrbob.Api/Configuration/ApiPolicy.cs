namespace Ohrbob.Api.Configuration;

public class ApiPolicy
{
    public string Name { get; set; } = "";
    public IEnumerable<string> Scopes { get; set; } = Array.Empty<string>();
    public ApiPolicy(string name, params IEnumerable<string> scopes)
    {
        Name = name;
        Scopes = scopes;
    }
}