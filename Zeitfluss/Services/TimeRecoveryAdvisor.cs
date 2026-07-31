using System.Runtime.InteropServices;
using Zeitfluss.Models;

namespace Zeitfluss.Services;

public sealed class TimeRecoveryAdvisor
{
    public RecoveryAssessment Assess(
        AppData data,
        DateTime now,
        DateTime? lastUserInputAt = null,
        RecoveryAdvisorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        options ??= new RecoveryAdvisorOptions();
        Validate(options);

        var active = data.Intervals
            .Where(interval => interval.EndedAt is null && interval.StartedAt <= now)
            .OrderByDescending(interval => interval.StartedAt)
            .FirstOrDefault();
        if (active is null)
            return new RecoveryAssessment(null, RecoverySignalKind.None, lastUserInputAt, TimeSpan.Zero, null, []);

        var openDuration = now - active.StartedAt;
        var signals = RecoverySignalKind.None;
        if (active.StartedAt.Date < now.Date) signals |= RecoverySignalKind.CrossedDayBoundary;
        if (openDuration >= options.LongRunningThreshold) signals |= RecoverySignalKind.ExcessiveDuration;

        var normalizedLastInput = NormalizeLastInput(lastUserInputAt, active.StartedAt, now);
        if (normalizedLastInput is not null && now - normalizedLastInput.Value >= options.IdleThreshold)
            signals |= RecoverySignalKind.UserIdle;

        if (signals == RecoverySignalKind.None)
            return new RecoveryAssessment(active.Id, signals, normalizedLastInput, openDuration, null, []);

        var suggestions = new List<RecoverySuggestion>();
        if (signals.HasFlag(RecoverySignalKind.UserIdle) && normalizedLastInput is { } lastInput)
        {
            suggestions.Add(CreateSuggestion(
                active,
                RecoverySuggestionKind.LastUserActivity,
                lastInput,
                "Die Erfassung beim letzten erkannten Eingabezeitpunkt beenden."));
        }

        var scheduledEnd = CalculateScheduledEnd(data, active, now);
        if (scheduledEnd is { } plannedEnd && plannedEnd >= active.StartedAt && plannedEnd <= now)
        {
            suggestions.Add(CreateSuggestion(
                active,
                RecoverySuggestionKind.ScheduledTargetReached,
                plannedEnd,
                "Die Erfassung beenden, als die verbleibende Regelarbeitszeit erreicht war."));
        }

        if (signals.HasFlag(RecoverySignalKind.CrossedDayBoundary))
        {
            var dayBoundary = active.StartedAt.Date.AddDays(1);
            if (dayBoundary >= active.StartedAt && dayBoundary <= now)
            {
                suggestions.Add(CreateSuggestion(
                    active,
                    RecoverySuggestionKind.DayBoundary,
                    dayBoundary,
                    "Die Erfassung am Ende des begonnenen Kalendertags schließen."));
            }
        }

        suggestions = suggestions
            .GroupBy(suggestion => suggestion.EndAt)
            .Select(group => group.First())
            .ToList();
        var recommended = suggestions.FirstOrDefault(suggestion => suggestion.Kind == RecoverySuggestionKind.LastUserActivity)
            ?? suggestions.FirstOrDefault(suggestion => suggestion.Kind == RecoverySuggestionKind.ScheduledTargetReached)
            ?? suggestions.FirstOrDefault();

        return new RecoveryAssessment(active.Id, signals, normalizedLastInput, openDuration, recommended, suggestions);
    }

    private static DateTime? NormalizeLastInput(DateTime? lastUserInputAt, DateTime intervalStart, DateTime now)
    {
        if (lastUserInputAt is null) return null;
        if (lastUserInputAt > now) return now;
        return lastUserInputAt < intervalStart ? intervalStart : lastUserInputAt;
    }

    private static DateTime? CalculateScheduledEnd(AppData data, WorkInterval active, DateTime now)
    {
        var date = DateOnly.FromDateTime(active.StartedAt);
        var target = TimeCalculator.TargetForDay(data, date);
        if (target <= TimeSpan.Zero) return null;

        var dayStart = date.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);
        var previousTicks = data.Intervals
            .Where(interval => interval.Id != active.Id)
            .Sum(interval =>
            {
                var intervalStart = TimeCalculator.EffectiveStart(interval);
                var intervalEnd = TimeCalculator.EffectiveEnd(interval, now);
                var overlapStart = intervalStart > dayStart ? intervalStart : dayStart;
                var overlapEnd = intervalEnd < dayEnd ? intervalEnd : dayEnd;
                return overlapEnd > overlapStart ? (overlapEnd - overlapStart).Ticks : 0;
            });
        var remainingAtStart = target - TimeSpan.FromTicks(previousTicks);
        if (remainingAtStart < TimeSpan.Zero) remainingAtStart = TimeSpan.Zero;

        var effectiveStart = TimeCalculator.EffectiveStart(active);
        var scheduledEnd = effectiveStart + remainingAtStart;
        return scheduledEnd > dayEnd ? dayEnd : scheduledEnd;
    }

    private static RecoverySuggestion CreateSuggestion(
        WorkInterval interval,
        RecoverySuggestionKind kind,
        DateTime endAt,
        string explanation)
    {
        var creditedEnd = interval.UsesFiveMinuteRounding
            ? TimeCalculator.RoundDownToFiveMinutes(endAt)
            : endAt;
        var effectiveStart = TimeCalculator.EffectiveStart(interval);
        if (creditedEnd < effectiveStart) creditedEnd = effectiveStart;
        return new RecoverySuggestion(kind, endAt, creditedEnd, explanation);
    }

    private static void Validate(RecoveryAdvisorOptions options)
    {
        if (options.LongRunningThreshold <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Die Langzeit-Schwelle muss größer als null sein.");
        if (options.IdleThreshold <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Die Leerlauf-Schwelle muss größer als null sein.");
    }
}

public interface IIdleTimeProvider
{
    DateTime GetLastInputAt(DateTime now);
}

public sealed class WindowsIdleTimeProvider : IIdleTimeProvider
{
    public DateTime GetLastInputAt(DateTime now)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Die Windows-Leerlaufzeit ist nur unter Windows verfügbar.");

        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
            throw new InvalidOperationException("Der letzte Windows-Eingabezeitpunkt konnte nicht gelesen werden.");

        var elapsedMilliseconds = unchecked((uint)Environment.TickCount - info.Time);
        return now - TimeSpan.FromMilliseconds(elapsedMilliseconds);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo inputInfo);
}
