# Zeitfluss

Zeitfluss ist eine kleine, lokale Arbeitszeiterfassung für Windows auf Basis von .NET 10 und WPF.

## Funktionen

- Mehrere Arbeitsintervalle pro Tag mit **Arbeit beginnen**, **Pause/Fortsetzen** und **Feierabend**
- Regelarbeitszeit für Montag bis Sonntag und validierte Gesamtwochenstunden
- Fortlaufender Zeitsaldo über Tage, Kalenderwochen, Monate und Jahre
- Statistikansicht nach Tagen, Wochen, Monaten und Jahren mit Soll, Ist, Periodendifferenz und übertragenem Gesamtsaldo
- Excel-kompatibler CSV-Export mit deutschem Semikolon-Format
- Frei verschiebbares Hauptfenster und ein 146 × 48 Pixel großer Kompaktmodus rechts oben
- Kompaktanzeige mit heutiger Gesamtarbeitszeit und grünem Statuspunkt bei laufender Erfassung
- Kompaktanzeige per Ziehen frei verschiebbar; ihre Position wird separat gespeichert
- Verlustfreie `.zeitfluss`-Datensicherung für Import und Export beim PC-Wechsel
- Lokale, atomar gespeicherte JSON-Daten ohne Cloud oder Benutzerkonto

## Starten

Zeitfluss kann als self-contained Single-File-EXE veröffentlicht werden und benötigt dann keine separate .NET-Installation:

```powershell
dotnet publish Zeitfluss\Zeitfluss.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Die fertige Anwendung liegt anschließend unter `publish/Zeitfluss.exe`.

Beim ersten Start beginnt die Saldoberechnung am aktuellen Tag. Die Daten werden unter `%LOCALAPPDATA%\Zeitfluss\arbeitszeiten.json` gespeichert. Ein offenes Arbeitsintervall bleibt auch nach einem Neustart erhalten.

## Bedienung

1. Über das Zahnrad Wochenstunden und Tagessollzeiten einstellen. Die Summe der sieben Tage muss den Wochenstunden entsprechen.
2. **Arbeit beginnen** startet ein Intervall.
3. **Pause** beendet das aktuelle Intervall; **Arbeit fortsetzen** beginnt ein weiteres.
4. **Feierabend** schließt das aktuelle Intervall und markiert den Tag als beendet. Bei Bedarf kann danach nochmals gestartet werden.
5. Über das Minus oben rechts wird Zeitfluss zum kleinen Zeitindikator. Er kann mit gedrückter Maustaste verschoben werden; ein kurzer Klick stellt das Hauptfenster wieder her.
6. Unter **Auswertung** zwischen Tagen, Wochen, Monaten und Jahren wechseln oder alle Tagesdaten als CSV exportieren.
7. Unter **Einstellungen → Datensicherung** lässt sich der vollständige Bestand als `.zeitfluss`-Datei exportieren und auf einem anderen PC importieren. Vor einem Import erstellt die App automatisch eine Vor-Import-Sicherung in `%LOCALAPPDATA%\Zeitfluss`.

CSV-Dateien sind für Excel und Auswertungen gedacht. Für eine vollständige Wiederherstellung müssen `.zeitfluss`-Sicherungen verwendet werden. Vor einem Sicherungsexport muss eine laufende Arbeitszeit pausiert werden.

## Entwickeln und prüfen

```powershell
dotnet build Zeitfluss\Zeitfluss.csproj -c Release
dotnet run --project Zeitfluss.LogicTests\Zeitfluss.LogicTests.csproj -c Release
dotnet run --project Zeitfluss.VisualSmoke\Zeitfluss.VisualSmoke.csproj -c Release -- visual-smoke
dotnet publish Zeitfluss\Zeitfluss.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Die Render-Smoke-Tests prüfen aktive, pausierte, beendete und kompakte Zustände sowie die Tagesauswertung. Prüfbilder werden nach `visual-smoke-v3/` geschrieben.

## Screenshots

![Zeitfluss Hauptfenster](docs/screenshots/zeitfluss-main.png)

![Datensicherung in den Einstellungen](docs/screenshots/zeitfluss-backup.png)
