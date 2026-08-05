---
status: testing
phase: 10-visual-styles
source: [10-01-SUMMARY.md, 10-02-SUMMARY.md]
started: 2026-03-20T19:25:00Z
updated: 2026-03-20T19:25:00Z
---

## Current Test
<!-- OVERWRITE each test - shows where we are -->

number: 4
name: Model Badge Pills (vollrund)
expected: |
  Die kleinen Badge-Labels neben dem Modellnamen sollten vollrunde Ecken haben —
  wie eine Kapsel/Pille, nicht leicht abgerundet.
awaiting: user response

## Tests

### 1. ProgressBar Track-Farbe (semi-transparent)
expected: Starte die App und schau auf die Fortschrittsbalken (Session-Nutzung, Wöchentliche Quota, Context Window). Der "Track" (Hintergrund des Balkens) sollte semi-transparent grau wirken (#72808080) — im Dark Mode etwas dunkler, im Light Mode etwas heller, aber NICHT mehr opaque schwarz oder weiß.
result: skipped
reason: User will später testen

### 2. Alle ProgressBars Höhe (6px)
expected: Alle Fortschrittsbalken (Context Window, Weekly Quota, Sonnet-Balken, und Subagent falls sichtbar) sollten einheitlich 6px hoch sein — schlanker als typische Default-ProgressBars, alle gleich hoch.
result: pass
note: Bug found and fixed inline — WinUI 3 ProgressBarTrackHeight defaulted to 1px, overridden to 6px

### 3. ComboBox-Styling (abgerundete Ecken)
expected: Die ComboBox für die Modell-Auswahl (Dropdown oben in der Ansicht) hat einen abgerundeten Hintergrund (CornerRadius=8) und einen segmentierten Hintergrund, der zum Tab-Bar passt. Öffne das Dropdown und prüfe ob es angenehm zum Tab-Bar-Stil passt.
result: pass

### 4. Model Badge Pills (vollrund)
expected: Die kleinen Badge-Labels neben dem Modellnamen (z.B. "claude-opus-4-5" als Pill-Badge) sollten vollrunde Ecken haben — wie eine Kapsel/Pille, nicht leicht abgerundet. CornerRadius=999 erzeugt maximal runde Ecken unabhängig von der Größe.
result: [pending]

### 5. Statistics Labels (sekundäre Textfarbe + Normal-Gewicht)
expected: In der Statistik-Sektion unten: die Beschriftungen "Total" und "Cost" (die Label-Texte, NICHT die Zahlen daneben) sollten in einer helleren/gedimmten Textfarbe erscheinen und normalem Schriftgewicht (nicht fett). Die Zahlenwerte daneben bleiben in der Primärfarbe/SemiBold.
result: [pending]

### 6. StatsTotal Abstand (8px oben)
expected: Zwischen dem "Total"-Label und dem Element darüber sollte ein sichtbarer Abstand von ca. 8px sein — eine kleine visuelle Trennung zwischen den Statistik-Gruppen.
result: [pending]

### 7. Chart Achsenbeschriftungen Farbe
expected: Im Nutzungsdiagramm (Win2D-Chart): die Achsenbeschriftungen (0%, 50%, 100% auf der Y-Achse und 0h, 1h, ..., 5h auf der X-Achse) sollten in derselben Farbe erscheinen wie der Timer-Countdown-Text — ein mittleres Grau (SecondaryTextBrush), NICHT mehr das dunklere TertiaryTextBrush-Grau.
result: [pending]

## Summary

total: 7
passed: 3
issues: 0
pending: 3
skipped: 1

## Gaps

[none yet]
