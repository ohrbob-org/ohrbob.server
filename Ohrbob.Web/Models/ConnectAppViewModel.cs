using Ohrbob.Server.Shared.Data;
using System.Collections.Generic;

namespace Ohrbob.Web.Models;

public class ConnectAppViewModel
{
    public List<Profile> Profiles { get; set; } = new();
}
