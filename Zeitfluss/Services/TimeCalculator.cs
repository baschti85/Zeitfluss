using System.Globalization;
using Zeitfluss.Models;

namespace Zeitfluss.Services;

public static class TimeCalculator
{
    public static DateTime RoundUpToFiveMinutes(DateTime value)
    {
        var truncated = new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute / 5 * 5, 0, value.Kind);
        return truncated < value ? truncated.AddMinutes(5) : truncated;
    }

    public static DateTime RoundDownToFiveMinutes(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute / 5 * 5, 0, value.Kind);

    public static DateTime EffectiveStart(WorkInterval interval) => interval.RoundedStartedAt ?? interval.StartedAt;

    public static DateTime EffectiveEnd(WorkInterval interval, DateTime now)
    {
        var end = interval.RoundedEndedAt ?? interval.EndedAt ?? (interval.UsesFiveMinuteRounding ? RoundDownToFiveMinutes(now) : now);
        return end < EffectiveStart(interval) ? EffectiveStart(interval) : end;
    }

    public static TimeSpan ActualForDay(AppData data, DateOnly date, DateTime now)
    {
        var dayStart = date.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);
        var ticks = data.Intervals.Sum(interval =>
        {
            var end = EffectiveEnd(interval, now);
            var effectiveStart = EffectiveStart(interval);
            var overlapStart = effectiveStart > dayStart ? effectiveStart : dayStart;
            var overlapEnd = end < dayEnd ? end : dayEnd;
            return overlapEnd > overlapStart ? (overlapEnd - overlapStart).Ticks : 0;
        });
        return TimeSpan.FromTicks(ticks);
    }

    public static TimeSpan TargetForDay(AppData data, DateOnly date) =>
        TimeSpan.FromHours(data.Settings.DailyHours.GetValueOrDefault(date.DayOfWeek));

    public static IReadOnlyList<DailySummary> Daily(AppData data, DateOnly through, DateTime now)
    {
        if (through < data.TrackingStartedOn) return [];
        var rows = new List<DailySummary>();
        var cumulative = TimeSpan.Zero;
        for (var date = data.TrackingStartedOn; date <= through; date = date.AddDays(1))
        {
            var target = TargetForDay(data, date);
            var actual = ActualForDay(data, date, now);
            var balance = actual - target;
            cumulative += balance;
            var dayStart = date.ToDateTime(TimeOnly.MinValue);
            var dayEnd = dayStart.AddDays(1);
            var intervals = string.Join(" | ", data.Intervals
                .Where(x => EffectiveStart(x) < dayEnd && EffectiveEnd(x, now) > dayStart)
                .OrderBy(EffectiveStart)
                .Select(x =>
                {
                    var start = EffectiveStart(x) < dayStart ? dayStart : EffectiveStart(x);
                    var end = EffectiveEnd(x, now);
                    var endLabel = x.EndedAt is null ? "offen" : end >= dayEnd ? "24:00" : end.ToString("HH:mm");
                    return $"{start:HH:mm}-{endLabel}";
                }));
            rows.Add(new DailySummary(date, target, actual, balance, cumulative, intervals));
        }
        return rows;
    }

    public static IReadOnlyList<PeriodSummary> Periods(AppData data, DateOnly through, DateTime now, PeriodKind kind)
    {
        var days = Daily(data, through, now);
        return days.GroupBy(day => PeriodKey(day.Date, kind))
            .Select(group =>
            {
                var first = group.First();
                var last = group.Last();
                var target = TimeSpan.FromTicks(group.Sum(x => x.Target.Ticks));
                var actual = TimeSpan.FromTicks(group.Sum(x => x.Actual.Ticks));
                return new PeriodSummary(PeriodLabel(first.Date, kind), first.Date, last.Date, target, actual, actual - target, last.Cumulative);
            })
            .OrderByDescending(x => x.Start)
            .ToList();
    }

    public static IReadOnlyList<IntervalDetail> DetailsForPeriod(AppData data, DateOnly start, DateOnly end, DateTime now)
    {
        var periodStart = start.ToDateTime(TimeOnly.MinValue);
        var periodEnd = end.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var details = new List<IntervalDetail>();
        foreach (var interval in data.Intervals
            .Where(interval => interval.StartedAt < periodEnd && (interval.EndedAt ?? now) > periodStart)
            .OrderBy(interval => interval.StartedAt))
        {
            var actualEnd = interval.EndedAt ?? now;
            var firstDate = DateOnly.FromDateTime(interval.StartedAt < periodStart ? periodStart : interval.StartedAt);
            var lastDate = DateOnly.FromDateTime(actualEnd >= periodEnd ? periodEnd.AddTicks(-1) : actualEnd);
            for (var date = firstDate; date <= lastDate; date = date.AddDays(1))
            {
                var dayStart = date.ToDateTime(TimeOnly.MinValue);
                var dayEnd = dayStart.AddDays(1);
                var rawStart = interval.StartedAt > dayStart ? interval.StartedAt : dayStart;
                var rawEnd = actualEnd < dayEnd ? actualEnd : dayEnd;
                var effectiveStart = EffectiveStart(interval);
                effectiveStart = effectiveStart < dayStart ? dayStart : effectiveStart > dayEnd ? dayEnd : effectiveStart;
                var effectiveEnd = EffectiveEnd(interval, now);
                effectiveEnd = effectiveEnd < dayStart ? dayStart : effectiveEnd > dayEnd ? dayEnd : effectiveEnd;
                if (effectiveEnd < effectiveStart) effectiveEnd = effectiveStart;
                var rawEndLabel = interval.EndedAt is null && date == DateOnly.FromDateTime(now) ? "offen" : rawEnd == dayEnd ? "24:00" : rawEnd.ToString("HH:mm");
                var hint = interval.UsesFiveMinuteRounding
                    ? $"5-Min.-Rundung · Roh: {rawStart:HH:mm}–{rawEndLabel}"
                    : string.Empty;
                details.Add(new IntervalDetail(date, effectiveStart, interval.EndedAt is null && date == DateOnly.FromDateTime(now) ? null : effectiveEnd, effectiveEnd - effectiveStart, hint));
            }
        }
        return details;
    }

    public static string FormatDuration(TimeSpan value, bool sign = false)
    {
        var prefix = value < TimeSpan.Zero ? "−" : sign && value > TimeSpan.Zero ? "+" : string.Empty;
        var absolute = value.Duration();
        return $"{prefix}{(int)absolute.TotalHours:00}:{absolute.Minutes:00}";
    }

    private static string PeriodKey(DateOnly date, PeriodKind kind) => kind switch
    {
        PeriodKind.Day => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        PeriodKind.Week => $"{ISOWeek.GetYear(date.ToDateTime(TimeOnly.MinValue))}-{ISOWeek.GetWeekOfYear(date.ToDateTime(TimeOnly.MinValue)):00}",
        PeriodKind.Month => $"{date.Year}-{date.Month:00}",
        _ => date.Year.ToString(CultureInfo.InvariantCulture)
    };

    private static string PeriodLabel(DateOnly date, PeriodKind kind) => kind switch
    {
        PeriodKind.Day => date.ToDateTime(TimeOnly.MinValue).ToString("ddd, dd. MMMM yyyy", CultureInfo.GetCultureInfo("de-DE")),
        PeriodKind.Week => $"KW {ISOWeek.GetWeekOfYear(date.ToDateTime(TimeOnly.MinValue)):00} · {ISOWeek.GetYear(date.ToDateTime(TimeOnly.MinValue))}",
        PeriodKind.Month => date.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", CultureInfo.GetCultureInfo("de-DE")),
        _ => date.Year.ToString(CultureInfo.InvariantCulture)
    };
}

public enum PeriodKind { Day, Week, Month, Year }
