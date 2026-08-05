---
status: complete
phase: 05-cost-analytics
source: [05-01-SUMMARY.md, 05-02-SUMMARY.md]
started: 2026-03-17T07:30:00Z
updated: 2026-03-17T07:35:00Z
---

## Current Test

[testing complete]

## Tests

### 1. Statistiken Tab-Leiste sichtbar
expected: Im Hauptfenster ist ein STATISTIKEN-Abschnitt mit einer Segmented-Tab-Leiste (Session, Heute, Woche, Monat) sichtbar. "Session" ist standardmäßig ausgewählt.
result: pass

### 2. Tab-Wechsel zeigt andere Werte
expected: Beim Klick auf "Heute" wechselt der ausgewählte Tab visuell. Die Statistik-Werte in der Tabelle ändern sich (da "Heute" mehr Daten als "Session" aggregiert). Gleiches für "Woche" und "Monat" — die Werte sollten jeweils gleich oder größer sein als der vorherige Zeitraum.
result: pass

### 3. Statistik-Datentabelle mit 7 Zeilen
expected: Die Statistik-Tabelle zeigt 7 Datenzeilen: Modelle, Eingabe, Ausgabe, Cache-Schreiben, Cache-Lesen, Gesamt, Kosten. Jede Zeile hat einen Label und einen Wert. (Burn Rate wurde entfernt und sollte NICHT angezeigt werden.)
result: pass

### 4. Token-Werte formatiert
expected: Token-Werte werden menschenlesbar formatiert (z.B. "1.2M" oder "450K" statt roher Zahlen wie "1200000"). Die Kosten werden als Dollar-Betrag angezeigt (z.B. "$1.23").
result: pass

### 5. Shimmer-Animation beim Laden
expected: Beim Wechsel von "Session" auf "Heute"/"Woche"/"Monat" erscheint kurzzeitig eine Shimmer-Animation (pulsierende Lade-Animation) auf den Statistik-Zeilen, bevor die aggregierten Werte eingeblendet werden. Bei "Session" gibt es kein Shimmer (Daten sind sofort verfügbar).
result: pass

### 6. Kosten-Berechnung plausibel
expected: Die angezeigten Kosten steigen mit dem Zeitraum (Session ≤ Heute ≤ Woche ≤ Monat). Die Werte sind plausibel — nicht $0.00 für Zeiträume mit Aktivität und nicht unrealistisch hoch.
result: pass

### 7. Settings: Preisdaten-Info
expected: In den Settings (Einstellungen) gibt es zwei neue Zeilen: "Preisdaten" (zeigt die Pricing-Quelle, z.B. "LiteLLM") und "Zuletzt aktualisiert" (zeigt wann die Preisdaten zuletzt geholt wurden).
result: pass

### 8. Modelle-Zeile zeigt verwendete Modelle
expected: Die "Modelle"-Zeile in der Statistik zeigt die Namen der Claude-Modelle an, die im gewählten Zeitraum verwendet wurden (z.B. "claude-sonnet-4-5-20250929").
result: pass

## Summary

total: 8
passed: 8
issues: 0
pending: 0
skipped: 0

## Gaps

[none yet]
