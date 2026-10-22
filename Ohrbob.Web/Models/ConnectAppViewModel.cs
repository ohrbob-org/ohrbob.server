using Ohrbob.Server.Shared.Data;
using System.Collections.Generic;

namespace Ohrbob.Web.Models;

public class ConnectAppViewModel
{
    public List<Profile> Profiles { get; set; } = new();

    /// <summary>Submitted key name, carried back so an error re-render keeps what the user typed.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Submitted profile scope, carried back so an error re-render keeps the selection.</summary>
    public string ProfileId { get; set; } = string.Empty;
}
