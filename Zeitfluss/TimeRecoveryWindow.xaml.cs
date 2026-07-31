using System.Windows;
using Zeitfluss.Models;
using Zeitfluss.Services;

namespace Zeitfluss;

public partial class TimeRecoveryWindow : Window
{
    private readonly RecoveryAssessment _assessment;

    public TimeRecoveryWindow(AppData data, RecoveryAssessment assessment)
    {
        InitializeComponent();
        _assessment = assessment;
        WindowAppearance.Apply(this, data.Settings);
        var active = data.Intervals.FirstOrDefault(interval => interval.Id == assessment.IntervalId);
        var now = DateTime.Now;
        StartedText.Text = active?.StartedAt.ToString("dd.MM. · HH:mm") ?? "–";
        OpenDurationText.Text = TimeCalculator.FormatDuration(assessment.OpenDuration);
        TodayText.Text = TimeCalculator.FormatDuration(TimeCalculator.ActualForDay(data, DateOnly.FromDateTime(now), now));
        ReasonText.Text = BuildReason(assessment.Signals);

        var choices = assessment.Suggestions.Select(ToChoice).ToList();
        SuggestionList.ItemsSource = choices;
        var recommendedIndex = assessment.Recommended is null ? 0 : assessment.Suggestions.ToList().FindIndex(item => item == assessment.Recommended);
        SuggestionList.SelectedIndex = Math.Max(0, recommendedIndex);
    }

    public RecoverySuggestion? SelectedSuggestion =>
        SuggestionList.SelectedItem is RecoveryChoice choice ? choice.Suggestion : null;

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSuggestion is null) return;
        DialogResult = true;
    }

    private void KeepRunning_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private static RecoveryChoice ToChoice(RecoverySuggestion suggestion)
    {
        var title = suggestion.Kind switch
        {
            RecoverySuggestionKind.LastUserActivity => "Bei letzter Aktivität beenden",
            RecoverySuggestionKind.ScheduledTargetReached => "Beim Erreichen des Tagessolls beenden",
            _ => "Am Tagesende beenden"
        };
        var creditLabel = suggestion.CreditedEndAt == suggestion.EndAt
            ? "voll angerechnet"
            : $"angerechnet bis {suggestion.CreditedEndAt:HH:mm}";
        return new RecoveryChoice(suggestion, title, suggestion.EndAt.ToString("dd.MM. · HH:mm"), creditLabel, suggestion.Explanation);
    }

    private static string BuildReason(RecoverySignalKind signals)
    {
        var reasons = new List<string>();
        if (signals.HasFlag(RecoverySignalKind.CrossedDayBoundary)) reasons.Add("Die Erfassung lief über Mitternacht hinaus");
        if (signals.HasFlag(RecoverySignalKind.ExcessiveDuration)) reasons.Add("sie ist ungewöhnlich lang geöffnet");
        if (signals.HasFlag(RecoverySignalKind.UserIdle)) reasons.Add("Windows hat eine längere Inaktivität erkannt");
        return reasons.Count == 0 ? "Diese Erfassung sollte kurz geprüft werden." : string.Join(" und ", reasons) + ". Zeitfluss verändert nichts ohne deine Bestätigung.";
    }

    private sealed record RecoveryChoice(RecoverySuggestion Suggestion, string Title, string EndLabel, string CreditLabel, string Explanation);
}
