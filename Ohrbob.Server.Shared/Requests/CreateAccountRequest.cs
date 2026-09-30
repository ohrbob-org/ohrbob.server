namespace Ohrbob.Server.Shared.Requests;

public class CreateAccountRequest
{
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ProfilePicture { get; set; } = string.Empty;
    public string IdentityProvider { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
}