namespace Zeitfluss.Models;

public enum WorkdayState
{
    Ready,
    Running,
    Paused,
    Finished
}

public sealed record WorkdayInsight(
    DateOnly Date,
    WorkdayState State,
    Guid? ActiveIntervalId,
    DateTime? ActiveSince,
    DateTime? CreditedSince,
    TimeSpan CurrentSessionElapsed,
    TimeSpan CurrentSessionCredited,
    TimeSpan Actual,
    TimeSpan Target,
    TimeSpan Remaining,
    TimeSpan DayBalance,
    TimeSpan CumulativeBalance,
    DateTime? ProjectedFinishAt,
    DateTime? FinishIfResumedNow);

[Flags]
public enum RecoverySignalKind
{
    None = 0,
    CrossedDayBoundary = 1,
    ExcessiveDuration = 2,
    UserIdle = 4
}

public enum RecoverySuggestionKind
{
    LastUserActivity,
    ScheduledTargetReached,
    DayBoundary
}

public sealed record RecoverySuggestion(
    RecoverySuggestionKind Kind,
    DateTime EndAt,
    DateTime CreditedEndAt,
    string Explanation);

public sealed record RecoveryAssessment(
    Guid? IntervalId,
    RecoverySignalKind Signals,
    DateTime? LastUserInputAt,
    TimeSpan OpenDuration,
    RecoverySuggestion? Recommended,
    IReadOnlyList<RecoverySuggestion> Suggestions)
{
    public bool RequiresReview => Signals != RecoverySignalKind.None;
}

public sealed record RecoveryAdvisorOptions
{
    public TimeSpan LongRunningThreshold { get; init; } = TimeSpan.FromHours(12);
    public TimeSpan IdleThreshold { get; init; } = TimeSpan.FromMinutes(45);
}

[Flags]
public enum GlobalHotKeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008,
    NoRepeat = 0x4000
}

public readonly record struct GlobalHotKeyGesture(uint VirtualKey, GlobalHotKeyModifiers Modifiers)
{
    public bool IsEmpty => VirtualKey == 0;
}

public sealed record GlobalHotKeyBindings(
    GlobalHotKeyGesture Start,
    GlobalHotKeyGesture PauseResume,
    GlobalHotKeyGesture Stop)
{
    public static GlobalHotKeyBindings Default { get; } = new(
        new GlobalHotKeyGesture(0x77, GlobalHotKeyModifiers.Control | GlobalHotKeyModifiers.Alt | GlobalHotKeyModifiers.NoRepeat),
        new GlobalHotKeyGesture(0x78, GlobalHotKeyModifiers.Control | GlobalHotKeyModifiers.Alt | GlobalHotKeyModifiers.NoRepeat),
        new GlobalHotKeyGesture(0x79, GlobalHotKeyModifiers.Control | GlobalHotKeyModifiers.Alt | GlobalHotKeyModifiers.NoRepeat));
}

public sealed record TrayIconState(
    string StatusText,
    TimeSpan TodayDuration,
    bool IsRunning,
    bool IsPaused,
    bool CanStop);
