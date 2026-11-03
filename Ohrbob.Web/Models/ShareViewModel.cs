using Ohrbob.Server.Shared.Requests;

namespace Ohrbob.Web.Models;

public class ShareViewModel
{
    public StationWithFeedsDto Station { get; set; } = new();
    public string Code { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public bool IsAuthenticated { get; set; }
    public bool IsOwner { get; set; }
    public bool AllProfilesSubscribed { get; set; }
    public List<ShareProfileOption> Profiles { get; set; } = new();
}

public class ShareProfileOption
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsSubscribed { get; set; }
    public bool IsActive { get; set; }
}
