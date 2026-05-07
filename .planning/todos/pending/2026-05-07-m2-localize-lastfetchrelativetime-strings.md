---
created: 2026-05-07
source: v1.4 code review
severity: major
area: ViewModels/SettingsViewModel.cs
related_phase: 22-ui-polish
related_backlog: backlog_pricing_never_loaded.md
---

# M-2: Localize `LastFetchRelativeTime` hardcoded strings

## Problem

`SettingsViewModel.cs:116-128` returns hardcoded English strings: `"Never"`, `"1 minute ago"`, `"X minutes ago"`. The code includes a `// v1.4 fallback` comment explicitly deferring proper resw keys.

## Why This Matters

The About-tab pricing-timestamp feature is currently invisible in production due to the separate pricing-service silent-failure bug (`backlog_pricing_never_loaded.md`) — the UI shows the literal string "Never" because pricing never loads. This means English speakers see "Never" and German speakers also see "Never", which is a visible localization gap that would have been caught in v1.4 UAT if the pricing service worked.

## Fix

Add 3 resw keys × 2 locales:

| Key | EN | DE |
|-----|----|----|
| `LastFetchNever` | Never | Nie |
| `LastFetchOneMinuteAgo` | 1 minute ago | vor 1 Minute |
| `LastFetchMinutesAgoFormat` | {0} minutes ago | vor {0} Minuten |

Replace hardcoded returns with `Localizer.Get().GetLocalizedString(key)` and `string.Format` for the placeholder variant.

Add to `ResourceCoverageTests` to enforce the new keys exist in both locales.

## Coupling Note

This fix is tightly coupled to `backlog_pricing_never_loaded.md` — both should ship together because the L10N gap is only user-visible once the pricing service works. Single phase / single plan covering both is the natural shape.

## Effort

S — 3 keys × 2 locales + 3 callsites + 1 test method.

## v1.5 Priority

Medium. Bundle with the pricing-service fix.
