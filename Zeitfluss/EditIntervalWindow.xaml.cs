using System.Globalization;
using System.Windows;
using Zeitfluss.Models;
using Zeitfluss.Services;

namespace Zeitfluss;

public partial class EditIntervalWindow : Window
{
    private static readonly CultureInfo GermanCulture = CultureInfo.GetCultureInfo("de-DE");
    private readonly AppData _data;
    private readonly WorkInterval _interval;

    public EditIntervalWindow(AppData data, WorkInterval interval)
    {
        InitializeComponent();
        _data = data;
        WindowAppearance.Apply(this, _data.Settings);
        _interval = interval;
        StartDateTextBox.Text = interval.StartedAt.ToString("dd.MM.yyyy", GermanCulture);
        StartTimeTextBox.Text = interval.StartedAt.ToString("HH:mm:ss", GermanCulture);
        var end = interval.EndedAt ?? DateTime.Now;
        EndDateTextBox.Text = end.ToString("dd.MM.yyyy", GermanCulture);
        EndTimeTextBox.Text = end.ToString("HH:mm:ss", GermanCulture);
        OngoingCheckBox.IsChecked = interval.EndedAt is null;
        OngoingCheckBox.IsEnabled = interval.EndedAt is null;
        RoundingInfoText.Text = interval.UsesFiveMinuteRounding
            ? "5-Minuten-Rhythmus aktiv · Rundungswerte werden automatisch neu berechnet."
            : "Für diesen Eintrag ist keine Rundung aktiv.";
        UpdateEndFields();
    }

    private void Ongoing_Changed(object sender, RoutedEventArgs e) => UpdateEndFields();

    private void UpdateEndFields()
    {
        if (EndDatePanel is null || EndTimePanel is null) return;
        var enabled = OngoingCheckBox.IsChecked != true;
        EndDatePanel.IsEnabled = enabled;
        EndTimePanel.IsEnabled = enabled;
        EndDatePanel.Opacity = enabled ? 1 : 0.42;
        EndTimePanel.Opacity = enabled ? 1 : 0.42;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        if (!TryParseMoment(StartDateTextBox.Text, StartTimeTextBox.Text, "Beginn", out var start)) return;

        DateTime? end = null;
        if (OngoingCheckBox.IsChecked != true)
        {
            if (!TryParseMoment(EndDateTextBox.Text, EndTimeTextBox.Text, "Ende", out var parsedEnd)) return;
            end = parsedEnd;
        }

        var error = IntervalEditor.Validate(_data, _interval.Id, start, end, DateTime.Now);
        if (error is not null) { ErrorText.Text = error; return; }

        IntervalEditor.Apply(_interval, start, end);
        DialogResult = true;
    }

    private bool TryParseMoment(string dateText, string timeText, string label, out DateTime value)
    {
        value = default;
        if (!DateOnly.TryParseExact(dateText.Trim(), "dd.MM.yyyy", GermanCulture, DateTimeStyles.None, out var date))
        {
            ErrorText.Text = $"{label}: Bitte das Datum als TT.MM.JJJJ eingeben.";
            return false;
        }

        var formats = new[] { "H:mm", "HH:mm", "H:mm:ss", "HH:mm:ss" };
        if (!TimeOnly.TryParseExact(timeText.Trim(), formats, GermanCulture, DateTimeStyles.None, out var time))
        {
            ErrorText.Text = $"{label}: Bitte eine Uhrzeit wie 08:30 oder 08:30:15 eingeben.";
            return false;
        }

        value = date.ToDateTime(time);
        return true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
