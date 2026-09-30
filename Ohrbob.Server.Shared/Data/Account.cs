using System;

namespace Ohrbob.Server.Shared.Data;

public class Account : DbEntry
{
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ProfilePicture { get; set; } = string.Empty;
    public IdpUser[] IdentityProviders { get; set; } = Array.Empty<IdpUser>();
    
}