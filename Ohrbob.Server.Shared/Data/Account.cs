using System;
using System.Collections.Generic;

namespace Ohrbob.Server.Shared.Data;

public class Account : DbEntry
{
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ProfilePicture { get; set; } = string.Empty;
    public List<IdpUser> IdentityProviders { get; set; } = new();
    public string LastUsedProfileId { get; set; } = string.Empty;
    public bool NeedsOnboarding { get; set; } = true;
}