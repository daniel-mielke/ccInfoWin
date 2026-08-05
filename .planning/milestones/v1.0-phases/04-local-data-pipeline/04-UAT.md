---
status: complete
phase: 04-local-data-pipeline
source: 04-01-SUMMARY.md, 04-02-SUMMARY.md, 04-03-SUMMARY.md
started: 2026-03-11T20:50:00Z
updated: 2026-03-16T09:30:00Z
---

## Current Test

[testing complete]

## Tests

### 1. Session Dropdown mit Aktiv/Inaktiv-Gruppierung
expected: Die Session-ComboBox zeigt alle JSONL-Sessions gruppiert in "Aktiv" und "Inaktiv". Jede Session hat einen lesbaren Anzeigenamen (Projektname / letztes Pfadsegment).
result: issue
reported: "App crashed after start due to CollectionViewSource InvalidCastException. After fix: dropdown works with flat list and colored activity indicators. Second issue: 'Unbekanntes Projekt' displayed for sessions without cwd. Fixed by implementing Tauri-style fallback chain in SessionNameHelper."
severity: blocker

### 2. Session-Auswahl ändert Dashboard-Daten
expected: Bei Auswahl einer anderen Session in der ComboBox aktualisieren sich Kontextfenster, Token-Zähler und alle session-spezifischen Anzeigen auf die Daten der neu gewählten Session.
result: pass

### 3. Session bleibt bei Datenaktualisierung erhalten (SESS-04)
expected: Wenn neue JSONL-Daten eintreffen (z.B. durch Claude Code Nutzung), bleibt die aktuell ausgewählte Session bestehen — kein automatischer Wechsel auf eine andere Session.
result: pass

### 4. Kontextfenster-Anzeige mit Progress Bar
expected: Der KONTEXTFENSTER-Bereich zeigt einen Fortschrittsbalken mit Prozentanzeige der Kontextauslastung. Daneben ein Model-Badge-Chip mit dem Modellnamen (z.B. "Claude 3.5 Sonnet").
result: issue
reported: "Model badge had gray background color instead of model-specific color. Fixed by adding GetBadgeColorHex to ModelContextLimits and binding badge background to model-dependent SolidColorBrush (Opus=purple, Sonnet=orange, Haiku=blue)."
severity: cosmetic

### 5. Autocompact-Warnung bei hoher Kontextauslastung
expected: Bei hoher Kontextauslastung (>90% bei großen Modellen, >95% bei kleinen) erscheint ein visueller Warnhinweis im Kontextfenster-Bereich.
result: skipped
reason: No session with high enough context utilization available for testing

### 6. Subagent-Balken im Kontextfenster
expected: Falls die aktive Session Subagents hat, werden deren Kontextfenster-Balken unterhalb des Haupt-Kontextfensters angezeigt (ItemsRepeater mit eigenen Progress Bars).
result: skipped
reason: No active subagents available for testing

### 7. Token-Zähler (Input/Output)
expected: Der TOKENS-Bereich zeigt Input- und Output-Token-Counts für die gewählte Session an. Große Zahlen werden mit K/M-Suffixen kompakt formatiert (z.B. "12.5K", "1.2M").
result: pass

### 8. Leere-Session-Platzhalter
expected: Wenn keine Session ausgewählt ist oder keine Sessions existieren, zeigt das Dashboard einen Platzhalter "Keine aktive Session" statt leerer Felder.
result: skipped
reason: Could not easily deselect current session in ComboBox

### 9. Session-Timeout in Einstellungen konfigurierbar
expected: In den Settings gibt es eine ComboBox für den Sitzungs-Timeout mit Optionen 15, 30, 60 und 120 Minuten. Die Auswahl wird gespeichert und überlebt App-Neustarts.
result: pass

### 10. Live-Updates bei JSONL-Änderungen
expected: Wenn Claude Code aktiv genutzt wird und neue JSONL-Daten geschrieben werden, aktualisiert sich das Dashboard automatisch ohne manuellen Refresh (FileSystemWatcher mit ~300ms Debounce).
result: pass

## Summary

total: 10
passed: 5
issues: 2
pending: 0
skipped: 3

## Gaps

- truth: "Session ComboBox shows all JSONL sessions grouped by Aktiv/Inaktiv with readable display names"
  status: resolved
  reason: "User reported: App crashed on start due to CollectionViewSource InvalidCastException. Fixed by replacing grouped CollectionViewSource with flat ObservableCollection<SessionDisplayItem> with colored activity dots. Second fix: 'Unbekanntes Projekt' replaced with Tauri-style fallback chain (cwd → decoded dir name → skip)."
  severity: blocker
  test: 1
  root_cause: "WinUI 3 WinRT COM interop cannot project SessionGroup : List<T>, IGrouping<string, T> through CollectionViewSource.Source setter. SessionNameHelper returned hardcoded 'Unbekanntes Projekt' instead of using fallback dir name decoding."
  artifacts:
    - path: "CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs"
      issue: "SessionGroup + GroupedSessions property incompatible with WinRT COM projection"
    - path: "CCInfoWindows/CCInfoWindows/Helpers/SessionNameHelper.cs"
      issue: "No fallback to decoded directory name when cwd is null"
  missing:
    - "Replaced with flat SessionDisplayItem collection with colored dot indicators"
    - "SessionNameHelper.GetDisplayName now accepts fallbackDirName parameter with Tauri-style decode chain"
  debug_session: ""

- truth: "Model badge has model-specific background color (Opus=purple, Sonnet=orange, Haiku=blue)"
  status: resolved
  reason: "User reported: Model badge had gray background instead of model-specific color. Fixed by adding GetBadgeColorHex to ModelContextLimits and binding badge to ContextModelBadgeColor SolidColorBrush."
  severity: cosmetic
  test: 4
  root_cause: "Badge Border used static ThemeResource ChartBackgroundBrush instead of model-dependent color"
  artifacts:
    - path: "CCInfoWindows/CCInfoWindows/Helpers/ModelContextLimits.cs"
      issue: "Missing GetBadgeColorHex method"
    - path: "CCInfoWindows/CCInfoWindows/Views/MainView.xaml"
      issue: "Badge Background bound to static theme brush"
  missing:
    - "Added GetBadgeColorHex with Opus=#BF5AF2, Sonnet=#FF9F0A, Haiku=#0A84FF"
    - "Added ContextModelBadgeColor property to MainViewModel"
    - "Updated XAML badge to bind Background, white text, SemiBold, CornerRadius=6"
  debug_session: ""
