namespace Ohrbob.Web.Models;

public class StreamViewModel
{
    public bool StreamEnabled { get; set; }
    public List<StreamProfileGroup> Profiles { get; set; } = new();
}

public class StreamProfileGroup
{
    public string ProfileName { get; set; } = string.Empty;
    public List<StreamStationLink> Stations { get; set; } = new();
}

public class StreamStationLink
{
    public string StationName { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}
