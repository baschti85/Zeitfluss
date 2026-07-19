using Zeitfluss.Models;
using Zeitfluss.Services;

var tests = new (string Name, Action Run)[]
{
    ("Mehrere Intervalle werden addiert", MultipleIntervals),
    ("Intervall über Mitternacht wird geteilt", CrossMidnight),
    ("Guthaben wird tagübergreifend fortgeschrieben", CumulativeBalance),
    ("Kalenderwochen werden getrennt aggregiert", WeekAggregation),
    ("Tage werden einzeln und absteigend aggregiert", DayAggregation),
    ("5-Minuten-Rundung rechnet Start und Ende korrekt", FiveMinuteRounding),
    ("5-Minuten-Rundung folgt den exakten Grenzzeiten", FiveMinuteBoundaryExamples),
    ("5-Minuten-Rundung erzeugt keine negative Zeit", RoundedShortInterval),
    ("Laufende Rundung wächst nur im Fünf-Minuten-Rhythmus", ActiveRoundedInterval),
    ("Details entsprechen dem Ist-Wert der Periode", DetailsMatchPeriodActual),
    ("Sicherung wird verlustfrei exportiert und importiert", BackupRoundTrip),
    ("Beschädigte Sicherung wird abgewiesen", InvalidBackupRejected),
    ("Import behält lokale Fensterpositionen", BackupKeepsLocalPlacement),
    ("Negative Dauer wird korrekt formatiert", NegativeFormatting)
};

foreach (var (name, run) in tests)
{
    run();
    Console.WriteLine($"PASS  {name}");
}

static AppData Data(DateOnly start) => new() { TrackingStartedOn = start };
static void Equal(TimeSpan expected, TimeSpan actual)
{
    if (expected != actual) throw new InvalidOperationException($"Erwartet {expected}, erhalten {actual}");
}

static void MultipleIntervals()
{
    var date = new DateOnly(2026, 7, 20); var data = Data(date);
    data.Intervals.Add(new() { StartedAt = date.ToDateTime(new TimeOnly(8, 0)), EndedAt = date.ToDateTime(new TimeOnly(12, 0)) });
    data.Intervals.Add(new() { StartedAt = date.ToDateTime(new TimeOnly(12, 30)), EndedAt = date.ToDateTime(new TimeOnly(16, 45)) });
    Equal(TimeSpan.FromHours(8.25), TimeCalculator.ActualForDay(data, date, date.ToDateTime(new TimeOnly(17, 0))));
}

static void CrossMidnight()
{
    var first = new DateOnly(2026, 7, 20); var second = first.AddDays(1); var data = Data(first);
    data.Intervals.Add(new() { StartedAt = first.ToDateTime(new TimeOnly(22, 0)), EndedAt = second.ToDateTime(new TimeOnly(2, 0)) });
    Equal(TimeSpan.FromHours(2), TimeCalculator.ActualForDay(data, first, second.ToDateTime(new TimeOnly(3, 0))));
    Equal(TimeSpan.FromHours(2), TimeCalculator.ActualForDay(data, second, second.ToDateTime(new TimeOnly(3, 0))));
}

static void CumulativeBalance()
{
    var monday = new DateOnly(2026, 7, 20); var data = Data(monday);
    data.Intervals.Add(new() { StartedAt = monday.ToDateTime(new TimeOnly(8, 0)), EndedAt = monday.ToDateTime(new TimeOnly(17, 0)) });
    data.Intervals.Add(new() { StartedAt = monday.AddDays(1).ToDateTime(new TimeOnly(8, 0)), EndedAt = monday.AddDays(1).ToDateTime(new TimeOnly(15, 0)) });
    var rows = TimeCalculator.Daily(data, monday.AddDays(1), monday.AddDays(1).ToDateTime(new TimeOnly(18, 0)));
    Equal(TimeSpan.FromHours(1), rows[0].Cumulative);
    Equal(TimeSpan.Zero, rows[1].Cumulative);
}

static void WeekAggregation()
{
    var sunday = new DateOnly(2026, 7, 19); var monday = sunday.AddDays(1); var data = Data(sunday);
    data.Intervals.Add(new() { StartedAt = sunday.ToDateTime(new TimeOnly(10, 0)), EndedAt = sunday.ToDateTime(new TimeOnly(11, 0)) });
    data.Intervals.Add(new() { StartedAt = monday.ToDateTime(new TimeOnly(8, 0)), EndedAt = monday.ToDateTime(new TimeOnly(16, 0)) });
    var periods = TimeCalculator.Periods(data, monday, monday.ToDateTime(new TimeOnly(17, 0)), PeriodKind.Week);
    if (periods.Count != 2) throw new InvalidOperationException($"Erwartet 2 Wochen, erhalten {periods.Count}");
}

static void NegativeFormatting()
{
    if (TimeCalculator.FormatDuration(TimeSpan.FromMinutes(-90), true) != "−01:30") throw new InvalidOperationException("Negatives Format ist falsch");
}

static void DayAggregation()
{
    var first = new DateOnly(2026, 7, 20); var second = first.AddDays(1); var data = Data(first);
    data.Intervals.Add(new() { StartedAt = first.ToDateTime(new TimeOnly(8, 0)), EndedAt = first.ToDateTime(new TimeOnly(17, 0)) });
    data.Intervals.Add(new() { StartedAt = second.ToDateTime(new TimeOnly(8, 0)), EndedAt = second.ToDateTime(new TimeOnly(16, 0)) });
    var periods = TimeCalculator.Periods(data, second, second.ToDateTime(new TimeOnly(17, 0)), PeriodKind.Day);
    if (periods.Count != 2 || periods[0].Start != second || periods[1].Start != first) throw new InvalidOperationException("Tagesreihenfolge ist falsch");
    Equal(TimeSpan.Zero, periods[0].Balance);
    Equal(TimeSpan.FromHours(1), periods[1].Balance);
}

static void FiveMinuteRounding()
{
    var date = new DateOnly(2026, 7, 20); var data = Data(date);
    data.Intervals.Add(Rounded(date.ToDateTime(new TimeOnly(12, 26)), date.ToDateTime(new TimeOnly(13, 2))));
    Equal(TimeSpan.FromMinutes(30), TimeCalculator.ActualForDay(data, date, date.ToDateTime(new TimeOnly(14, 0))));
    if (TimeCalculator.RoundUpToFiveMinutes(date.ToDateTime(new TimeOnly(12, 26))) != date.ToDateTime(new TimeOnly(12, 30))) throw new InvalidOperationException("Start wurde nicht aufgerundet");
    if (TimeCalculator.RoundDownToFiveMinutes(date.ToDateTime(new TimeOnly(13, 2))) != date.ToDateTime(new TimeOnly(13, 0))) throw new InvalidOperationException("Ende wurde nicht abgerundet");
}

static void FiveMinuteBoundaryExamples()
{
    var date = new DateOnly(2026, 7, 20); var data = Data(date);
    var startAt1228 = date.ToDateTime(new TimeOnly(12, 28));
    var at1232 = date.ToDateTime(new TimeOnly(12, 32));

    if (TimeCalculator.RoundUpToFiveMinutes(startAt1228) != date.ToDateTime(new TimeOnly(12, 30))) throw new InvalidOperationException("Start um 12:28 muss erst ab 12:30 zählen");
    if (TimeCalculator.RoundUpToFiveMinutes(at1232) != date.ToDateTime(new TimeOnly(12, 35))) throw new InvalidOperationException("Start um 12:32 muss erst ab 12:35 zählen");
    if (TimeCalculator.RoundDownToFiveMinutes(at1232) != date.ToDateTime(new TimeOnly(12, 30))) throw new InvalidOperationException("Ende um 12:32 darf nur bis 12:30 zählen");

    data.Intervals.Add(Rounded(startAt1228, at1232));
    Equal(TimeSpan.Zero, TimeCalculator.ActualForDay(data, date, date.ToDateTime(new TimeOnly(13, 0))));
}

static void RoundedShortInterval()
{
    var date = new DateOnly(2026, 7, 20); var data = Data(date);
    data.Intervals.Add(Rounded(date.ToDateTime(new TimeOnly(12, 26)), date.ToDateTime(new TimeOnly(12, 27))));
    Equal(TimeSpan.Zero, TimeCalculator.ActualForDay(data, date, date.ToDateTime(new TimeOnly(13, 0))));
    var detail = TimeCalculator.DetailsForPeriod(data, date, date, date.ToDateTime(new TimeOnly(13, 0))).Single();
    Equal(TimeSpan.Zero, detail.Duration);
    if (detail.RoundingHint.Length == 0) throw new InvalidOperationException("Rundungshinweis fehlt");
}

static void ActiveRoundedInterval()
{
    var date = new DateOnly(2026, 7, 20); var interval = new WorkInterval { StartedAt = date.ToDateTime(new TimeOnly(12, 26)), UsesFiveMinuteRounding = true, RoundedStartedAt = date.ToDateTime(new TimeOnly(12, 30)) };
    Equal(TimeSpan.Zero, TimeCalculator.EffectiveEnd(interval, date.ToDateTime(new TimeOnly(12, 34))) - TimeCalculator.EffectiveStart(interval));
    Equal(TimeSpan.FromMinutes(5), TimeCalculator.EffectiveEnd(interval, date.ToDateTime(new TimeOnly(12, 35))) - TimeCalculator.EffectiveStart(interval));
}

static void DetailsMatchPeriodActual()
{
    var first = new DateOnly(2026, 7, 20); var second = first.AddDays(1); var data = Data(first);
    data.Intervals.Add(Rounded(first.ToDateTime(new TimeOnly(23, 58)), second.ToDateTime(new TimeOnly(0, 7))));
    var now = second.ToDateTime(new TimeOnly(1, 0));
    var details = TimeCalculator.DetailsForPeriod(data, first, second, now);
    Equal(TimeSpan.FromMinutes(5), TimeSpan.FromTicks(details.Sum(x => x.Duration.Ticks)));
    Equal(TimeSpan.Zero, TimeCalculator.ActualForDay(data, first, now));
    Equal(TimeSpan.FromMinutes(5), TimeCalculator.ActualForDay(data, second, now));
}

static WorkInterval Rounded(DateTime start, DateTime end) => new()
{
    StartedAt = start,
    EndedAt = end,
    UsesFiveMinuteRounding = true,
    RoundedStartedAt = TimeCalculator.RoundUpToFiveMinutes(start),
    RoundedEndedAt = TimeCalculator.RoundDownToFiveMinutes(end)
};

static void BackupRoundTrip()
{
    var date = new DateOnly(2026, 7, 20); var data = Data(date);
    data.Settings.CompactWindowLeft = 321.5;
    data.Settings.CompactWindowTop = 87.25;
    data.Settings.UseFiveMinuteRounding = true;
    data.Intervals.Add(Rounded(date.ToDateTime(new TimeOnly(8, 1)), date.ToDateTime(new TimeOnly(16, 32))));
    data.FinishedDays.Add(date);
    var path = Path.Combine(Path.GetTempPath(), $"zeitfluss-{Guid.NewGuid():N}.zeitfluss");
    try
    {
        BackupService.Export(path, data);
        var imported = BackupService.Import(path);
        if (imported.TrackingStartedOn != date || imported.Intervals.Count != 1 || !imported.FinishedDays.Contains(date)) throw new InvalidOperationException("Arbeitszeitdaten fehlen nach dem Import");
        if (imported.Settings.CompactWindowLeft != 321.5 || imported.Settings.CompactWindowTop != 87.25) throw new InvalidOperationException("Kompaktposition fehlt nach dem Import");
        if (!imported.Settings.UseFiveMinuteRounding || !imported.Intervals[0].UsesFiveMinuteRounding || imported.Intervals[0].RoundedStartedAt is null) throw new InvalidOperationException("Rundungseinstellung fehlt nach dem Import");
    }
    finally { if (File.Exists(path)) File.Delete(path); }
}

static void InvalidBackupRejected()
{
    var path = Path.Combine(Path.GetTempPath(), $"zeitfluss-{Guid.NewGuid():N}.zeitfluss");
    try
    {
        File.WriteAllText(path, "{ keine Sicherung }");
        try { BackupService.Import(path); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Beschädigte Sicherung wurde akzeptiert");
    }
    finally { if (File.Exists(path)) File.Delete(path); }
}

static void BackupKeepsLocalPlacement()
{
    var local = Data(new DateOnly(2026, 7, 20));
    local.Settings.WindowLeft = 100;
    local.Settings.WindowTop = 200;
    local.Settings.CompactWindowLeft = 300;
    local.Settings.CompactWindowTop = 400;
    var imported = Data(new DateOnly(2025, 1, 1));
    imported.Settings.WindowLeft = 9000;
    imported.Settings.WindowTop = 9000;
    imported.Settings.CompactWindowLeft = 9000;
    imported.Settings.CompactWindowTop = 9000;
    BackupService.PreserveLocalWindowPlacement(local, imported);
    if (imported.Settings.WindowLeft != 100 || imported.Settings.WindowTop != 200 || imported.Settings.CompactWindowLeft != 300 || imported.Settings.CompactWindowTop != 400)
        throw new InvalidOperationException("Lokale Fensterpositionen wurden überschrieben");
}
