using Zeitfluss.Models;

namespace Zeitfluss.Services;

public static class IntervalEditor
{
    public static string? Validate(AppData data, Guid intervalId, DateTime start, DateTime? end, DateTime now)
    {
        var editedInterval = data.Intervals.FirstOrDefault(interval => interval.Id == intervalId);
        if (editedInterval is null)
            return "Die ausgewählte Erfassung wurde nicht gefunden.";
        if (start == default)
            return "Bitte gib einen gültigen Beginn ein.";
        if (DateOnly.FromDateTime(start) < data.TrackingStartedOn)
            return $"Der Beginn darf nicht vor dem Start der Zeiterfassung am {data.TrackingStartedOn:dd.MM.yyyy} liegen.";
        if (start > now)
            return "Der Beginn darf nicht in der Zukunft liegen.";
        if (end is not null && end <= start)
            return "Das Ende muss nach dem Beginn liegen.";
        if (end is not null && end > now)
            return "Das Ende darf nicht in der Zukunft liegen.";
        if (end is null && editedInterval.EndedAt is not null)
            return "Eine abgeschlossene Erfassung kann nicht wieder geöffnet werden.";

        var candidateEnd = end ?? DateTime.MaxValue;
        var overlaps = data.Intervals
            .Where(interval => interval.Id != intervalId)
            .Any(interval => start < (interval.EndedAt ?? DateTime.MaxValue) && interval.StartedAt < candidateEnd);
        return overlaps ? "Die korrigierte Zeit überschneidet sich mit einer anderen Erfassung." : null;
    }

    public static void Apply(WorkInterval interval, DateTime start, DateTime? end)
    {
        interval.StartedAt = start;
        interval.EndedAt = end;
        interval.RoundedStartedAt = interval.UsesFiveMinuteRounding ? TimeCalculator.RoundUpToFiveMinutes(start) : null;
        interval.RoundedEndedAt = interval.UsesFiveMinuteRounding && end is not null ? TimeCalculator.RoundDownToFiveMinutes(end.Value) : null;
    }
}
