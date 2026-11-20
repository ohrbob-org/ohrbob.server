namespace Ohrbob.Api.Services;

/// <summary>How often a feed is expected to publish, as deduced from its feed metadata.</summary>
public enum FeedRefreshCadence
{
    Hourly,
    Daily,
    Weekly,
    Periodic,
    Monthly
}

/// <summary>When a feed should next be polled, and how that decision was reached.</summary>
public sealed record FeedRefreshPlan(
    DateTime NextUpdateAt,
    FeedRefreshCadence Cadence,
    TimeSpan? PublishTimeOfDay,
    bool FromRrule);

/// <summary>
/// Decides when a feed should next be polled. Podcasts are expensive to fetch and most
/// publish on a rhythm, so the schedule is deduced instead of polling every feed hourly:
/// <list type="number">
/// <item>the Podcasting 2.0 <c>podcast:updateFrequency rrule</c>, when the feed declares one;</item>
/// <item>otherwise the publish dates — a time of day shared by more than three episodes
/// is treated as the publishing slot, and the gap between episodes as the cadence;</item>
/// <item>otherwise the historic hourly fallback.</item>
/// </list>
/// Every scheduled poll happens one minute after the expected publishing slot.
/// </summary>
public static class FeedRefreshScheduler
{
    /// <summary>Polls run one minute after the publishing slot, never during it.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(1);

    private const int MaxLookaheadDays = 800;
    private const double DailyMedianMaxDays = 2.0;
    private const double MonthlyMedianMinDays = 25.0;
    private const double WeeklyMedianMinDays = 2.5;

    public static FeedRefreshPlan Compute(
        DateTime now,
        string? updateFrequencyRrule,
        DateTime? lastUpdated,
        IEnumerable<DateTime?> publishedDates)
    {
        now = NormalizeUtc(now);
        var published = (publishedDates ?? Enumerable.Empty<DateTime?>())
            .Where(d => d.HasValue)
            .Select(d => NormalizeUtc(d!.Value))
            .OrderBy(d => d)
            .ToList();

        if (!string.IsNullOrWhiteSpace(updateFrequencyRrule))
        {
            var anchor = published.Count > 0 ? published[^1] : (lastUpdated.HasValue ? NormalizeUtc(lastUpdated.Value) : now);
            var fromRrule = TryComputeFromRrule(updateFrequencyRrule, anchor, now);
            if (fromRrule != null) return fromRrule;
        }

        return ComputeFromPublishDates(published, lastUpdated, now);
    }

    private static FeedRefreshPlan ComputeFromPublishDates(List<DateTime> published, DateTime? lastUpdated, DateTime now)
    {
        var fallback = HourlyPlan(now, lastUpdated);

        if (published.Count < 4)
            return fallback;

        // "Same time of publishing": the minute of day shared by the most episodes.
        var slot = published
            .GroupBy(d => (int)d.TimeOfDay.TotalMinutes)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Max())
            .First();

        if (slot.Count() <= 3)
            return fallback;

        var slotDates = slot.OrderBy(d => d).ToList();
        var timeOfDay = TimeSpan.FromMinutes(slot.Key);
        var gaps = slotDates
            .Zip(slotDates.Skip(1), (previous, next) => (next - previous).TotalDays)
            .OrderBy(days => days)
            .ToList();
        var medianGap = gaps[gaps.Count / 2];
        var lastDate = slotDates[^1];

        if (medianGap <= DailyMedianMaxDays)
        {
            var periodDays = Math.Max(1, (int)Math.Round(medianGap));
            return AdvanceByDays(
                lastDate, timeOfDay, periodDays, FeedRefreshCadence.Daily, now);
        }

        if (medianGap >= MonthlyMedianMinDays)
        {
            var periodMonths = Math.Max(1, (int)Math.Round(medianGap / 30.44));
            return AdvanceByMonths(lastDate, periodMonths, now, timeOfDay);
        }

        var publishingDays = slotDates.Select(d => d.DayOfWeek).Distinct().ToList();
        if (publishingDays.Count <= 3 && medianGap >= WeeklyMedianMinDays)
        {
            var intervalWeeks = Math.Max(1, (int)Math.Round(medianGap / 7.0));
            return AdvanceByWeekdays(lastDate, timeOfDay, publishingDays, intervalWeeks, now);
        }

        var periodicDays = Math.Max(1, (int)Math.Round(medianGap));
        return AdvanceByDays(
            lastDate, timeOfDay, periodicDays, FeedRefreshCadence.Periodic, now);
    }

    private static FeedRefreshPlan AdvanceByDays(
        DateTime lastSlot, TimeSpan timeOfDay, int periodDays, FeedRefreshCadence cadence, DateTime now)
    {
        if (IsStale(lastSlot, periodDays, now))
            return new FeedRefreshPlan(now.AddDays(1), cadence, timeOfDay, false);

        var candidate = DateTime.SpecifyKind(lastSlot.Date.AddDays(periodDays) + timeOfDay, DateTimeKind.Utc);
        var guard = 0;
        while (candidate + Grace <= now && guard++ < MaxLookaheadDays)
            candidate = candidate.AddDays(periodDays);

        return new FeedRefreshPlan(candidate + Grace, cadence, timeOfDay, false);
    }

    private static FeedRefreshPlan AdvanceByMonths(DateTime lastSlot, int periodMonths, DateTime now, TimeSpan timeOfDay)
    {
        if (IsStale(lastSlot, periodMonths * 30.44, now))
            return new FeedRefreshPlan(now.AddDays(1), FeedRefreshCadence.Monthly, timeOfDay, false);

        var candidate = DateTime.SpecifyKind(lastSlot.AddMonths(periodMonths), DateTimeKind.Utc);
        var guard = 0;
        while (candidate + Grace <= now && guard++ < 240)
            candidate = candidate.AddMonths(periodMonths);

        return new FeedRefreshPlan(candidate + Grace, FeedRefreshCadence.Monthly, timeOfDay, false);
    }

    private static FeedRefreshPlan AdvanceByWeekdays(
        DateTime lastSlot, TimeSpan timeOfDay, List<DayOfWeek> publishingDays, int intervalWeeks, DateTime now)
    {
        var periodDays = 7.0 / publishingDays.Count * intervalWeeks;
        if (IsStale(lastSlot, periodDays, now))
            return new FeedRefreshPlan(now.AddDays(1), FeedRefreshCadence.Weekly, timeOfDay, false);

        var anchorWeekStart = StartOfWeek(lastSlot.Date);
        for (var offset = 0; offset <= MaxLookaheadDays; offset++)
        {
            var date = lastSlot.Date.AddDays(offset);
            if (!publishingDays.Contains(date.DayOfWeek)) continue;

            var weekIndex = (int)Math.Round((StartOfWeek(date) - anchorWeekStart).TotalDays / 7);
            if (weekIndex % intervalWeeks != 0) continue;

            var candidate = DateTime.SpecifyKind(date + timeOfDay, DateTimeKind.Utc);
            if (candidate < lastSlot) continue;
            if (candidate + Grace > now)
                return new FeedRefreshPlan(candidate + Grace, FeedRefreshCadence.Weekly, timeOfDay, false);
        }

        return new FeedRefreshPlan(now.AddDays(1), FeedRefreshCadence.Weekly, timeOfDay, false);
    }

    private static bool IsStale(DateTime lastSlot, double periodDays, DateTime now) =>
        (now - lastSlot).TotalDays > Math.Max(1.0, periodDays) * 3;

    private static FeedRefreshPlan HourlyPlan(DateTime now, DateTime? lastUpdated)
    {
        var due = lastUpdated.HasValue ? NormalizeUtc(lastUpdated.Value).AddHours(1) : now.AddHours(1);
        if (due < now) due = now;
        return new FeedRefreshPlan(due, FeedRefreshCadence.Hourly, null, false);
    }

    private static FeedRefreshPlan? TryComputeFromRrule(string rrule, DateTime anchor, DateTime now)
    {
        var parts = ParseRrule(rrule);
        if (parts == null) return null;

        anchor = NormalizeUtc(anchor);
        var timesOfDay = parts.ByHour.HasValue
            ? new[] { TimeSpan.FromHours(parts.ByHour.Value) + TimeSpan.FromMinutes(parts.ByMinute ?? 0) }
            : new[] { anchor.TimeOfDay };

        for (var offset = 0; offset <= MaxLookaheadDays; offset++)
        {
            var date = anchor.Date.AddDays(offset);
            if (!MatchesRrule(parts, anchor, date)) continue;

            foreach (var timeOfDay in timesOfDay.OrderBy(t => t))
            {
                var candidate = DateTime.SpecifyKind(date + timeOfDay, DateTimeKind.Utc);
                if (candidate < anchor) continue;
                if (candidate + Grace > now)
                    return new FeedRefreshPlan(candidate + Grace, CadenceFor(parts.Frequency), timeOfDay, true);
            }
        }

        return null;
    }

    private static bool MatchesRrule(RruleParts parts, DateTime anchor, DateTime date)
    {
        switch (parts.Frequency)
        {
            case RruleFrequency.Daily:
                return (date - anchor.Date).Days % parts.Interval == 0;

            case RruleFrequency.Weekly:
            {
                var weekIndex = (int)Math.Round((StartOfWeek(date) - StartOfWeek(anchor.Date)).TotalDays / 7);
                if (weekIndex % parts.Interval != 0) return false;

                var days = parts.ByDay is { Count: > 0 } ? parts.ByDay : new HashSet<DayOfWeek> { anchor.DayOfWeek };
                return days.Contains(date.DayOfWeek);
            }

            case RruleFrequency.Monthly:
            {
                var monthIndex = (date.Year - anchor.Year) * 12 + (date.Month - anchor.Month);
                return monthIndex >= 0 && monthIndex % parts.Interval == 0 && date.Day == anchor.Day;
            }

            case RruleFrequency.Yearly:
            {
                var yearIndex = date.Year - anchor.Year;
                return yearIndex >= 0 && yearIndex % parts.Interval == 0
                    && date.Month == anchor.Month && date.Day == anchor.Day;
            }

            default:
                return false;
        }
    }

    private static FeedRefreshCadence CadenceFor(RruleFrequency frequency) => frequency switch
    {
        RruleFrequency.Daily => FeedRefreshCadence.Daily,
        RruleFrequency.Weekly => FeedRefreshCadence.Weekly,
        _ => FeedRefreshCadence.Monthly
    };

    private enum RruleFrequency { Daily, Weekly, Monthly, Yearly }

    private sealed record RruleParts(
        RruleFrequency Frequency,
        int Interval,
        HashSet<DayOfWeek>? ByDay,
        int? ByHour,
        int? ByMinute);

    /// <summary>
    /// Parses the subset of RFC 5545 rules podcasters actually put in
    /// <c>podcast:updateFrequency</c>: FREQ, INTERVAL, BYDAY, BYHOUR and BYMINUTE.
    /// Returns <c>null</c> when the rule is missing or not understood.
    /// </summary>
    private static RruleParts? ParseRrule(string rrule)
    {
        var rule = rrule.Trim();
        if (rule.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase))
            rule = rule["RRULE:".Length..];

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in rule.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0) continue;
            fields[part[..separator].Trim()] = part[(separator + 1)..].Trim();
        }

        if (!fields.TryGetValue("FREQ", out var freqText)) return null;
        RruleFrequency frequency;
        switch (freqText.ToUpperInvariant())
        {
            case "DAILY": frequency = RruleFrequency.Daily; break;
            case "WEEKLY": frequency = RruleFrequency.Weekly; break;
            case "MONTHLY": frequency = RruleFrequency.Monthly; break;
            case "YEARLY": frequency = RruleFrequency.Yearly; break;
            default: return null;
        }

        var interval = 1;
        if (fields.TryGetValue("INTERVAL", out var intervalText))
        {
            if (!int.TryParse(intervalText, out interval) || interval < 1) return null;
        }

        HashSet<DayOfWeek>? byDay = null;
        if (fields.TryGetValue("BYDAY", out var byDayText))
        {
            byDay = new HashSet<DayOfWeek>();
            foreach (var raw in byDayText.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var day = ParseDay(raw.Trim());
                if (day == null) return null;
                byDay.Add(day.Value);
            }
            if (byDay.Count == 0) return null;
        }

        int? byHour = null;
        if (fields.TryGetValue("BYHOUR", out var hourText))
        {
            if (!int.TryParse(hourText, out var hour) || hour is < 0 or > 23) return null;
            byHour = hour;
        }

        int? byMinute = null;
        if (fields.TryGetValue("BYMINUTE", out var minuteText))
        {
            if (!int.TryParse(minuteText, out var minute) || minute is < 0 or > 59) return null;
            byMinute = minute;
        }

        return new RruleParts(frequency, interval, byDay, byHour, byMinute);
    }

    private static DayOfWeek? ParseDay(string value)
    {
        // Ordinal prefixes ("1MO", "-2FR") are legal RFC 5545; only the day matters here.
        if (value.Length < 2) return null;
        return value[^2..].ToUpperInvariant() switch
        {
            "MO" => DayOfWeek.Monday,
            "TU" => DayOfWeek.Tuesday,
            "WE" => DayOfWeek.Wednesday,
            "TH" => DayOfWeek.Thursday,
            "FR" => DayOfWeek.Friday,
            "SA" => DayOfWeek.Saturday,
            "SU" => DayOfWeek.Sunday,
            _ => null
        };
    }

    private static DateTime StartOfWeek(DateTime date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7; // Monday = 0
        return date.Date.AddDays(-offset);
    }

    private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
