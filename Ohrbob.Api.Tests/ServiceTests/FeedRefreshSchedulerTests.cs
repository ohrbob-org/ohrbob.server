using Ohrbob.Api.Services;

namespace Ohrbob.Api.Tests.ServiceTests;

public class FeedRefreshSchedulerTests
{
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    // The expected poll happens one minute after the publishing slot, so the system is
    // never fetching exactly while the host is still publishing.
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(1);

    private static DateTime Utc(int year, int month, int day, int hour, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    private static FeedRefreshPlan Compute(
        IEnumerable<DateTime> published,
        DateTime? lastUpdated = null,
        string? rrule = null,
        DateTime? now = null)
    {
        return FeedRefreshScheduler.Compute(
            now ?? Now,
            rrule,
            lastUpdated,
            published.Cast<DateTime?>().ToList());
    }

    [Test]
    public void Compute_WeeklyEpisodesAtSameTime_PollsNextWeekOneMinuteAfterSlot()
    {
        var published = new[]
        {
            Utc(2026, 10, 6, 6), Utc(2026, 10, 13, 6),
            Utc(2026, 10, 20, 6), Utc(2026, 10, 27, 6),
            Utc(2026, 11, 3, 6)
        };

        var plan = Compute(published, lastUpdated: Utc(2026, 11, 3, 6, 30));

        Assert.That(plan.Cadence, Is.EqualTo(FeedRefreshCadence.Weekly));
        Assert.That(plan.PublishTimeOfDay, Is.EqualTo(TimeSpan.FromHours(6)));
        Assert.That(plan.NextUpdateAt, Is.EqualTo(Utc(2026, 11, 10, 6).Add(Grace)));
    }

    [Test]
    public void Compute_DailyEpisodesAtSameTime_PollsNextDayOneMinuteAfterSlot()
    {
        var published = Enumerable.Range(0, 8)
            .Select(i => Utc(2026, 10, 20, 5).AddDays(i))
            .ToArray();

        var plan = Compute(
            published,
            lastUpdated: Utc(2026, 10, 27, 6),
            now: Utc(2026, 10, 27, 6));

        Assert.That(plan.Cadence, Is.EqualTo(FeedRefreshCadence.Daily));
        Assert.That(plan.NextUpdateAt, Is.EqualTo(Utc(2026, 10, 28, 5).Add(Grace)));
    }

    [Test]
    public void Compute_TwiceWeeklyEpisodes_PollsEveryMatchingWeekday()
    {
        // Mondays and Thursdays at 07:00 — two publishing days per week.
        var published = new[]
        {
            Utc(2026, 10, 1, 7), Utc(2026, 10, 5, 7), Utc(2026, 10, 8, 7), Utc(2026, 10, 12, 7),
            Utc(2026, 10, 15, 7), Utc(2026, 10, 19, 7), Utc(2026, 10, 22, 7), Utc(2026, 10, 26, 7),
            Utc(2026, 10, 29, 7), Utc(2026, 11, 2, 7)
        };

        var plan = Compute(published, lastUpdated: Utc(2026, 11, 2, 8));

        Assert.That(plan.Cadence, Is.EqualTo(FeedRefreshCadence.Weekly));
        Assert.That(plan.NextUpdateAt, Is.EqualTo(Utc(2026, 11, 5, 7).Add(Grace)));
    }

    [Test]
    public void Compute_OnlyThreeEpisodesShareTheSlot_FallsBackToHourly()
    {
        var published = new[]
        {
            Utc(2026, 10, 20, 6), Utc(2026, 10, 27, 6), Utc(2026, 11, 3, 6)
        };

        var plan = Compute(published, lastUpdated: Utc(2026, 11, 4, 11));

        Assert.That(plan.Cadence, Is.EqualTo(FeedRefreshCadence.Hourly));
        Assert.That(plan.NextUpdateAt, Is.EqualTo(Utc(2026, 11, 4, 12)));
    }

    [Test]
    public void Compute_NoEpisodes_FallsBackToHourly()
    {
        var plan = Compute(Array.Empty<DateTime>(), lastUpdated: Utc(2026, 11, 4, 11));

        Assert.That(plan.Cadence, Is.EqualTo(FeedRefreshCadence.Hourly));
        Assert.That(plan.NextUpdateAt, Is.EqualTo(Utc(2026, 11, 4, 12)));
    }

    [Test]
    public void Compute_StaleWeeklyPattern_PollsDailyInstead()
    {
        // Last episode 40 days ago: the show may have moved or stopped, so keep a
        // cheap daily safety net instead of waiting another week.
        var published = new[]
        {
            Utc(2026, 8, 25, 6), Utc(2026, 9, 1, 6), Utc(2026, 9, 8, 6), Utc(2026, 9, 15, 6),
            Utc(2026, 9, 22, 6), Utc(2026, 9, 29, 6)
        };

        var plan = Compute(published, lastUpdated: Utc(2026, 9, 29, 7));

        Assert.That(plan.NextUpdateAt, Is.EqualTo(Now.AddDays(1)));
    }

    [Test]
    public void Compute_WeeklyRrule_PollsNextMatchingDayOneMinuteAfterSlot()
    {
        // Newest episode Tue 2026-11-03 06:00; now is Wed afternoon, so Wednesday
        // already passed and the next slot is Monday.
        var published = new[] { Utc(2026, 10, 27, 6), Utc(2026, 11, 3, 6) };

        var plan = Compute(published, lastUpdated: Utc(2026, 11, 3, 7), rrule: "FREQ=WEEKLY;BYDAY=MO,WE");

        Assert.That(plan.FromRrule, Is.True);
        Assert.That(plan.Cadence, Is.EqualTo(FeedRefreshCadence.Weekly));
        Assert.That(plan.NextUpdateAt, Is.EqualTo(Utc(2026, 11, 9, 6).Add(Grace)));
    }

    [Test]
    public void Compute_DailyRruleWithIntervalAndExplicitTime_UsesTheRruleTime()
    {
        var published = new[] { Utc(2026, 11, 3, 6) };

        var plan = Compute(published, rrule: "FREQ=DAILY;INTERVAL=2;BYHOUR=6;BYMINUTE=30");

        Assert.That(plan.FromRrule, Is.True);
        Assert.That(plan.Cadence, Is.EqualTo(FeedRefreshCadence.Daily));
        Assert.That(plan.NextUpdateAt, Is.EqualTo(Utc(2026, 11, 5, 6, 30).Add(Grace)));
    }

    [Test]
    public void Compute_RruleWinsOverPublishDatePattern()
    {
        // Pubdates look daily, but the feed declares a weekly Monday cadence.
        var published = Enumerable.Range(0, 8)
            .Select(i => Utc(2026, 10, 20, 5).AddDays(i))
            .ToArray();

        var plan = Compute(published, rrule: "FREQ=WEEKLY;BYDAY=MO");

        Assert.That(plan.FromRrule, Is.True);
        Assert.That(plan.Cadence, Is.EqualTo(FeedRefreshCadence.Weekly));
        Assert.That(plan.NextUpdateAt.DayOfWeek, Is.EqualTo(DayOfWeek.Monday));
    }

    [Test]
    public void Compute_UnusableRrule_FallsBackToPublishDatePattern()
    {
        var published = new[]
        {
            Utc(2026, 10, 6, 6), Utc(2026, 10, 13, 6), Utc(2026, 10, 20, 6),
            Utc(2026, 10, 27, 6), Utc(2026, 11, 3, 6)
        };

        var plan = Compute(published, lastUpdated: Utc(2026, 11, 3, 6, 30), rrule: "FREQ=WEEKLY;BYDAY=XX");

        Assert.That(plan.FromRrule, Is.False);
        Assert.That(plan.NextUpdateAt, Is.EqualTo(Utc(2026, 11, 10, 6).Add(Grace)));
    }

    [Test]
    public void Compute_MonthlyEpisodesAtSameTime_PollsNextMonthOneMinuteAfterSlot()
    {
        var published = new[]
        {
            Utc(2026, 6, 15, 9), Utc(2026, 7, 15, 9), Utc(2026, 8, 15, 9),
            Utc(2026, 9, 15, 9), Utc(2026, 10, 15, 9)
        };

        var plan = Compute(published, lastUpdated: Utc(2026, 10, 15, 10));

        Assert.That(plan.Cadence, Is.EqualTo(FeedRefreshCadence.Monthly));
        Assert.That(plan.NextUpdateAt, Is.EqualTo(Utc(2026, 11, 15, 9).Add(Grace)));
    }
}
