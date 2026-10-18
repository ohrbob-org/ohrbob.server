using System.Collections.Generic;

namespace Ohrbob.Server.Shared.Requests;

/// <summary>
/// Request to add a batch of feeds (by URL) to an existing station.
/// </summary>
public class FeedImportRequest
{
    public List<string> Urls { get; set; } = new List<string>();
}
