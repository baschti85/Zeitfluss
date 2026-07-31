using Zeitfluss.Models;

namespace Zeitfluss.Services;

public sealed class WorkdayInsightService
{
    public WorkdayInsight Create(AppData data, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(data);

        var date = DateOnly.FromDateTime(now);
        var active = data.Intervals
            .Where(interval => interval.EndedAt is null)
            .OrderByDescending(interval => interval.StartedAt)
            .FirstOrDefault();
        var hasWorkToday = data.Intervals.Any(interval => OverlapsDate(interval, date, now));
        var finished = data.FinishedDays.Contains(date) && active is null;
        var state = active is not null
            ? WorkdayState.Running
            : finished
                ? WorkdayState.Finished
                : hasWorkToday ? WorkdayState.Paused : WorkdayState.Ready;

        var actual = TimeCalculator.ActualForDay(data, date, now);
        var target = TimeCalculator.TargetForDay(data, date);
        var remaining = actual < target ? target - actual : TimeSpan.Zero;
        var cumulative = TimeCalculator.Daily(data, date, now).LastOrDefault()?.Cumulative ?? TimeSpan.Zero;

        DateTime? projectedFinish = null;
        DateTime? finishIfResumedNow = null;
        var currentElapsed = TimeSpan.Zero;
        var currentCredited = TimeSpan.Zero;
        DateTime? creditedSince = null;

        if (active is not null)
        {
            creditedSince = TimeCalculator.EffectiveStart(active);
            currentElapsed = TimeCalculator.RecordedDuration(active, now);
            currentCredited = TimeCalculator.EffectiveEnd(active, now) - creditedSince.Value;
            if (currentCredited < TimeSpan.Zero) currentCredited = TimeSpan.Zero;

            var projectionBase = creditedSince > now ? creditedSince.Value : now;
            projectedFinish = projectionBase + remaining;
        }
        else if (!finished)
        {
            finishIfResumedNow = now + remaining;
        }

        return new WorkdayInsight(
            date,
            state,
            active?.Id,
            active?.StartedAt,
            creditedSince,
            currentElapsed,
            currentCredited,
            actual,
            target,
            remaining,
            actual - target,
            cumulative,
            projectedFinish,
            finishIfResumedNow);
    }

    private static bool OverlapsDate(WorkInterval interval, DateOnly date, DateTime now)
    {
        var start = date.ToDateTime(TimeOnly.MinValue);
        var end = start.AddDays(1);
        return interval.StartedAt < end && (interval.EndedAt ?? now) > start;
    }
}
