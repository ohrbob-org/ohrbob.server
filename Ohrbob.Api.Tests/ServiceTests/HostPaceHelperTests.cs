using Ohrbob.Server.Shared.Data;
using Ohrbob.Server.Shared.Helpers;

namespace Ohrbob.Api.Tests.ServiceTests;

public class HostPaceHelperTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private static Station Station(bool enabled = true) => new()
    {
        Id = "st-1",
        AccountId = "owner",
        HostPaceEnabled = enabled,
        HostProfileId = "host-profile",
        Feeds = new[] { "feed" }
    };

    private static List<Episode> Episodes() => new()
    {
        new Episode { Id = "ep-1", FeedUrl = "feed", PublishedAt = Now.AddDays(-3) },
        new Episode { Id = "ep-2", FeedUrl = "feed", PublishedAt = Now.AddDays(-2) },
        new Episode { Id = "ep-3", FeedUrl = "feed", PublishedAt = Now.AddDays(-1) }
    };

    [Test]
    public void GetUnavailableEpisodeIds_SubscriberOnlyGetsHostCompletedEpisodes()
    {
        var unavailable = HostPaceHelper.GetUnavailableEpisodeIds(
            Station(), "subscriber", Episodes(), new HashSet<string>(), new HashSet<string> { "ep-1", "ep-2" });

        Assert.That(unavailable, Is.EquivalentTo(new[] { "ep-3" }));
    }

    [Test]
    public void GetUnavailableEpisodeIds_HostProfileIsNotRestricted()
    {
        var unavailable = HostPaceHelper.GetUnavailableEpisodeIds(
            Station(), "host-profile", Episodes(), new HashSet<string>(), new HashSet<string>());

        Assert.That(unavailable, Is.Empty);
    }

    [Test]
    public void GetUnavailableEpisodeIds_OtherOwnerProfileIsRestricted()
    {
        var unavailable = HostPaceHelper.GetUnavailableEpisodeIds(
            Station(), "other-owner-profile", Episodes(), new HashSet<string>(), new HashSet<string> { "ep-1" });

        Assert.That(unavailable, Is.EquivalentTo(new[] { "ep-2", "ep-3" }));
    }

    [Test]
    public void GetUnavailableEpisodeIds_DisabledHostPaceIsNotRestricted()
    {
        var unavailable = HostPaceHelper.GetUnavailableEpisodeIds(
            Station(enabled: false), "subscriber", Episodes(), new HashSet<string>(), new HashSet<string>());

        Assert.That(unavailable, Is.Empty);
    }

    [Test]
    public void GetUnavailableEpisodeIds_StillAppliesAgingAndSubscriberCompletion()
    {
        var station = Station();
        station.IsAging = true;

        var unavailable = HostPaceHelper.GetUnavailableEpisodeIds(
            station, "subscriber", Episodes(),
            new HashSet<string> { "ep-2" },
            new HashSet<string> { "ep-1", "ep-2", "ep-3" });

        Assert.That(unavailable, Does.Contain("ep-1"), "older episodes are aged out");
        Assert.That(unavailable, Does.Contain("ep-2"), "subscriber already completed it");
    }

    [Test]
    public void IsEpisodeAllowed_RequiresHostCompletionForSubscribers()
    {
        var episodes = Episodes();

        Assert.That(HostPaceHelper.IsEpisodeAllowed(Station(), "subscriber", episodes[0], new HashSet<string>()), Is.False);
        Assert.That(HostPaceHelper.IsEpisodeAllowed(Station(), "subscriber", episodes[0], new HashSet<string> { "ep-1" }), Is.True);
        Assert.That(HostPaceHelper.IsEpisodeAllowed(Station(), "host-profile", episodes[0], new HashSet<string>()), Is.True);
        Assert.That(HostPaceHelper.IsEpisodeAllowed(Station(), "other-owner-profile", episodes[0], new HashSet<string>()), Is.False);
    }
}
