---
status: resolved
phase: 15-footer-tooltip-accessibility
source: [15-01-SUMMARY.md]
started: 2026-04-12T20:20:00+02:00
updated: 2026-04-13T00:05:00+02:00
---

## Current Test

[testing complete]

## Tests

### 1. Refresh Button Tooltip
expected: Hover over the Refresh button in the footer bar. A tooltip appears showing "Refresh" (en-US) or "Aktualisieren" (de-DE) depending on language.
result: issue
reported: "nope, bei keinem der drei buttons wird ein tooltip angezeigt"
severity: major

### 2. Settings Button Tooltip
expected: Hover over the Settings button (gear icon) in the footer bar. A tooltip appears showing "Settings" (en-US) or "Einstellungen" (de-DE) depending on language.
result: issue
reported: "nope, bei keinem der drei buttons wird ein tooltip angezeigt"
severity: major

### 3. Quit Button Tooltip
expected: Hover over the Quit button in the footer bar. A tooltip appears showing "Quit" (en-US) or "Beenden" (de-DE) depending on language.
result: issue
reported: "nope, bei keinem der drei buttons wird ein tooltip angezeigt"
severity: major

## Summary

total: 3
passed: 0
issues: 3
pending: 0
skipped: 0
blocked: 0

## Gaps

- truth: "Footer buttons show localized tooltips on hover"
  status: resolved
  reason: "User reported: nope, bei keinem der drei buttons wird ein tooltip angezeigt"
  severity: major
  test: 1
  root_cause: "WinUI3Localizer l:Uids.Uid runtime property injection for ToolTipService.ToolTip does not create tooltip infrastructure at XAML parse time. Explicit attribute needed in source XAML."
  artifacts:
    - path: "CCInfoWindows/CCInfoWindows/Views/MainView.xaml"
      issue: "Buttons only had l:Uids.Uid, no explicit ToolTipService.ToolTip in source XAML"
  missing: []
  debug_session: ""
