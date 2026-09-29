using System;

namespace Ohrbob.Server.Shared.Data;

public class Account
{
    public string Id { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string[] IdentityProviders { get; set; } = Array.Empty<string>();
}