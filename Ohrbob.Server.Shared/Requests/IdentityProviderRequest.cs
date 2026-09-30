namespace Ohrbob.Server.Shared.Requests;

public class IdentityProviderRequest
{
    public string Issuer { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
}