namespace Zeitfluss.Models;

public sealed class AppData
{
    public DateOnly TrackingStartedOn { get; set; } = DateOnly.FromDateTime(DateTime.Now);
    public AppSettings Settings { get; set; } = new();
    public List<WorkInterval> Intervals { get; set; } = [];
    public HashSet<DateOnly> FinishedDays { get; set; } = [];
}

public sealed class AppSettings
{
    public double WeeklyHours { get; set; } = 40;
    public Dictionary<DayOfWeek, double> DailyHours { get; set; } = new()
    {
        [DayOfWeek.Monday] = 8,
        [DayOfWeek.Tuesday] = 8,
        [DayOfWeek.Wednesday] = 8,
        [DayOfWeek.Thursday] = 8,
        [DayOfWeek.Friday] = 8,
        [DayOfWeek.Saturday] = 0,
        [DayOfWeek.Sunday] = 0
    };
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? CompactWindowLeft { get; set; }
    public double? CompactWindowTop { get; set; }
    public bool AlwaysOnTop { get; set; } = true;
    public bool UseFiveMinuteRounding { get; set; }
}

public sealed class WorkInterval
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public bool UsesFiveMinuteRounding { get; set; }
    public DateTime? RoundedStartedAt { get; set; }
    public DateTime? RoundedEndedAt { get; set; }
}

public sealed record DailySummary(DateOnly Date, TimeSpan Target, TimeSpan Actual, TimeSpan Balance, TimeSpan Cumulative, string Intervals);

public sealed record PeriodSummary(string Label, DateOnly Start, DateOnly End, TimeSpan Target, TimeSpan Actual, TimeSpan Balance, TimeSpan Cumulative);

public sealed record IntervalDetail(Guid IntervalId, DateOnly Date, DateTime Start, DateTime? End, TimeSpan Duration, string RoundingHint);
