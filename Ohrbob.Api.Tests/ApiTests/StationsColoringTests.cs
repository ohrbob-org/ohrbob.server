using NUnit.Framework;
using Ohrbob.Server.Shared.Data;
using System.Collections.Generic;
using System.Linq;

namespace Ohrbob.Api.Tests;

[TestFixture]
public class StationsColoringTests
{
    [Test]
    public void AssignsDistinctColorsFromPalette()
    {
        var schedule = new StationSchedule
        {
            Feeds = new List<StationFeedSchedule>
            {
                new StationFeedSchedule { Url = "https://feed1.url" },
                new StationFeedSchedule { Url = "https://feed2.url" }
            }
        };

        var palette = new[]
        {
            "#E63946", "#1D3557", "#457B9D", "#2A9D8F", "#E9C46A", "#F4A261",
            "#E76F51", "#6A057F", "#264653", "#8AB17D", "#D62828", "#FF9F1C"
        };

        for (int i = 0; i < schedule.Feeds.Count; i++)
        {
            var feed = schedule.Feeds[i];
            if (string.IsNullOrEmpty(feed.Color))
            {
                var assignedColors = schedule.Feeds.Select(f => f.Color).Where(c => !string.IsNullOrEmpty(c)).ToHashSet();
                var availableColor = palette.FirstOrDefault(c => !assignedColors.Contains(c));
                feed.Color = availableColor ?? palette[i % palette.Length];
            }
        }

        Assert.That(schedule.Feeds[0].Color, Is.EqualTo("#E63946"));
        Assert.That(schedule.Feeds[1].Color, Is.EqualTo("#1D3557"));
        Assert.That(schedule.Feeds[0].Color, Is.Not.EqualTo(schedule.Feeds[1].Color));
    }
}
