using System.Collections.Generic;

namespace Ohrbob.Server.Shared.Requests;

/// <summary>
/// Outcome of adding a batch of feeds to a station.
/// </summary>
public class FeedImportResultDto
{
    /// <summary>Feeds that were newly added to the station.</summary>
    public List<string> Added { get; set; } = new List<string>();

    /// <summary>Feeds that were already part of the station.</summary>
    public List<string> AlreadyPresent { get; set; } = new List<string>();

    /// <summary>Feeds that could not be read as RSS/Atom and were skipped.</summary>
    public List<string> Invalid { get; set; } = new List<string>();

    /// <summary>Feeds that were valid but did not fit the station's feed limit.</summary>
    public List<string> NotAddedDueToLimit { get; set; } = new List<string>();
}
