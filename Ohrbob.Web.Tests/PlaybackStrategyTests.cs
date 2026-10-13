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

    [Test]
    public void GetOrderedPlaylist_WithEpisodesToPlay_RespectsTurnLimits()
    {
        var station = new Station
        {
            Id = "test-station",
            Schedule = new StationSchedule
            {
                Feeds = new List<StationFeedSchedule>
                {
                    new StationFeedSchedule { Url = "https://feed1.url", EpisodesToPlay = 2 },
                    new StationFeedSchedule { Url = "https://feed2.url", EpisodesToPlay = 1 }
                }
            }
        };

        var episodes = new List<Episode>
        {
            new Episode { Id = "ep1-1", FeedUrl = "https://feed1.url", PublishedAt = DateTime.UtcNow.AddMinutes(1) },
            new Episode { Id = "ep1-2", FeedUrl = "https://feed1.url", PublishedAt = DateTime.UtcNow.AddMinutes(2) },
            new Episode { Id = "ep1-3", FeedUrl = "https://feed1.url", PublishedAt = DateTime.UtcNow.AddMinutes(3) },
            new Episode { Id = "ep2-1", FeedUrl = "https://feed2.url", PublishedAt = DateTime.UtcNow.AddMinutes(1) },
            new Episode { Id = "ep2-2", FeedUrl = "https://feed2.url", PublishedAt = DateTime.UtcNow.AddMinutes(2) }
        };

        var result = PlaybackStrategyHelper.GetOrderedPlaylist(episodes, station, new HashSet<string>(), null);

        // Expected order:
        // Turn 1 (feed 1, up to 2 episodes): ep1-1, ep1-2
        // Turn 1 (feed 2, up to 1 episode): ep2-1
        // Turn 2 (feed 1, up to 2 episodes): ep1-3
        // Turn 2 (feed 2, up to 1 episode): ep2-2
        var orderedIds = result.Select(e => e.Id).ToList();
        Assert.That(orderedIds, Is.EqualTo(new List<string> { "ep1-1", "ep1-2", "ep2-1", "ep1-3", "ep2-2" }));
    }

    [Test]
    public void GetOrderedPlaylist_WhenMarkedListened_MovesPlayheadAndUpcomingCorrectly()
    {
        var station = new Station
        {
            Id = "test-station",
            Schedule = new StationSchedule
            {
                Feeds = new List<StationFeedSchedule>
                {
                    new StationFeedSchedule { Url = "https://feed1.url", EpisodesToPlay = 2 },
                    new StationFeedSchedule { Url = "https://feed2.url", EpisodesToPlay = 1 }
                }
            }
        };

        var episodes = new List<Episode>
        {
            new Episode { Id = "ep1-1", FeedUrl = "https://feed1.url", PublishedAt = DateTime.UtcNow.AddMinutes(1) },
            new Episode { Id = "ep1-2", FeedUrl = "https://feed1.url", PublishedAt = DateTime.UtcNow.AddMinutes(2) },
            new Episode { Id = "ep1-3", FeedUrl = "https://feed1.url", PublishedAt = DateTime.UtcNow.AddMinutes(3) },
            new Episode { Id = "ep2-1", FeedUrl = "https://feed2.url", PublishedAt = DateTime.UtcNow.AddMinutes(1) },
            new Episode { Id = "ep2-2", FeedUrl = "https://feed2.url", PublishedAt = DateTime.UtcNow.AddMinutes(2) }
        };

        // Full initial playlist sequence
        var fullPlaylist = PlaybackStrategyHelper.GetOrderedPlaylist(episodes, station, new HashSet<string>(), null);
        Assert.That(fullPlaylist.Select(e => e.Id).ToList(), Is.EqualTo(new List<string> { "ep1-1", "ep1-2", "ep2-1", "ep1-3", "ep2-2" }));

        // Mark the first two episodes as listened/completed
        var listenedIds = new HashSet<string> { "ep1-1", "ep1-2" };

        // Get upcoming playlist (listened episodes excluded, conforming to feed rotation order starting with Feed 1)
        var upcomingPlaylist = PlaybackStrategyHelper.GetOrderedPlaylist(episodes, station, listenedIds, null);
        Assert.That(upcomingPlaylist.Select(e => e.Id).ToList(), Is.EqualTo(new List<string> { "ep1-3", "ep2-1", "ep2-2" }));

        // Get completed playlist (only completed ones, aligned with the full playback sequence order)
        var completedPlaylist = fullPlaylist.Where(e => listenedIds.Contains(e.Id)).ToList();
        Assert.That(completedPlaylist.Select(e => e.Id).ToList(), Is.EqualTo(new List<string> { "ep1-1", "ep1-2" }));

        // Assert that the lists are completely disjoint and contain all 5 episodes
        var combinedPlaylist = completedPlaylist.Concat(upcomingPlaylist).Select(e => e.Id).ToHashSet();
        Assert.That(combinedPlaylist, Is.EquivalentTo(fullPlaylist.Select(e => e.Id).ToHashSet()));
    }

    [Test]
    public void GetListenedAndAgedEpisodeIds_WhenStationIsAging_MarksOlderEpisodesAsListened()
    {
        var station = new Station
        {
            Id = "aging-station",
            IsAging = true,
            Feeds = new[] { "https://feed.url" }
        };

        var episodes = new List<Episode>
        {
            new Episode { Id = "ep-oldest", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-2) },
            new Episode { Id = "ep-middle", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow.AddDays(-1) },
            new Episode { Id = "ep-newest", FeedUrl = "https://feed.url", PublishedAt = DateTime.UtcNow }
        };

        var listened = new HashSet<string> { "ep-oldest" };

        var result = PlaybackStrategyHelper.GetListenedAndAgedEpisodeIds(station, episodes, listened);

        Assert.That(result.Contains("ep-oldest"), Is.True);
        Assert.That(result.Contains("ep-middle"), Is.True, "Older episode should be treated as listened");
        Assert.That(result.Contains("ep-newest"), Is.False, "Newest episode should NOT be treated as listened");
    }
}
