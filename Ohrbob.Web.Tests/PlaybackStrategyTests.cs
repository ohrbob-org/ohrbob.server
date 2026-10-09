using NUnit.Framework;
using Ohrbob.Server.Shared.Data;
using Ohrbob.Server.Shared.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Ohrbob.Web.Tests;

[TestFixture]
public class PlaybackStrategyTests
{
    [Test]
    public void GetOrderedPlaylist_WithRandomizedSortOrder_ShufflesDeterministically()
    {
        var stationId = "stable-station-id-123";
        var station = new Station
        {
            Id = stationId,
            Feeds = new[] { "https://feed.url" },
            Schedule = new StationSchedule
            {
                Feeds = new List<StationFeedSchedule>
                {
                    new StationFeedSchedule { Url = "https://feed.url", SortOrder = FeedSortOrder.Randomized }
                }
            }
        };

        var episodes = Enumerable.Range(1, 10).Select(i => new Episode
        {
            Id = $"ep-{i}",
            FeedUrl = "https://feed.url",
            PublishedAt = DateTime.UtcNow.AddMinutes(i)
        }).ToList();

        var listened = new HashSet<string>();

        // Run multiple times to assert deterministic stable shuffle
        var playlist1 = PlaybackStrategyHelper.GetOrderedPlaylist(episodes, station, listened, null);
        var playlist2 = PlaybackStrategyHelper.GetOrderedPlaylist(episodes, station, listened, null);

        Console.WriteLine($"playlist1: {string.Join(", ", playlist1.Select(e => e.Id))}");

        Assert.That(playlist1.Count, Is.EqualTo(10));
        // Verify results are identical across calls (stability)
        Assert.That(playlist1.Select(e => e.Id), Is.EqualTo(playlist2.Select(e => e.Id)));
        
        // Run with a different station ID seed and assert different shuffle
        var otherStation = new Station
        {
            Id = "different-station-id",
            Feeds = new[] { "https://feed.url" },
            Schedule = new StationSchedule
            {
                Feeds = new List<StationFeedSchedule>
                {
                    new StationFeedSchedule { Url = "https://feed.url", SortOrder = FeedSortOrder.Randomized }
                }
            }
        };
        var playlistOther = PlaybackStrategyHelper.GetOrderedPlaylist(episodes, otherStation, listened, null);
        Console.WriteLine($"playlistOther: {string.Join(", ", playlistOther.Select(e => e.Id))}");
        Assert.That(playlist1.Select(e => e.Id), Is.Not.EqualTo(playlistOther.Select(e => e.Id)));
    }
}
