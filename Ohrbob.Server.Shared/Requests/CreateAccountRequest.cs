namespace Ohrbob.Server.Shared.Requests;

public class CreateAccountRequest
{
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string ProfilePicture { get; set; } = "";
}