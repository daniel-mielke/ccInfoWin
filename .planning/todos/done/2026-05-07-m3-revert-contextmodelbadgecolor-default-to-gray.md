---
created: 2026-05-07
source: v1.4 code review
severity: major
area: ViewModels/MainViewModel.cs
related_phase: 22-ui-polish
resolves_phase: 28
---

# M-3: Revert `_contextModelBadgeColor = null!` to a real default

## Problem

`MainViewModel.cs:213` was changed from `new(Microsoft.UI.Colors.Gray)` to `null!`. The null-forgiving operator tells the compiler "trust me" — but between construction and the first poll (line 756: `ParseHexBrush(...)`), the property is genuinely null. WinUI 3 tolerates `null` for `SolidColorBrush` bindings (renders transparent), so no crash — but the original `Gray` default was semantically correct.

## Why This Matters

A null-coalesced "trust me" default that happens to render transparent is a latent bug masquerading as nothing. If the binding ever changes from a `SolidColorBrush` to something stricter (a converter that doesn't accept null, a custom drawing path), the app breaks at startup before the first poll completes.

## Fix

Either:

**Option A** — restore the explicit gray default:
```csharp
private SolidColorBrush _contextModelBadgeColor = new(Microsoft.UI.Colors.Gray);
```

**Option B** — initialize with a sensible badge color from the existing helper:
```csharp
private SolidColorBrush _contextModelBadgeColor = ParseHexBrush(ModelContextLimits.GetBadgeColorHex(null));
```

Prefer A unless `Gray` is wrong for the unbound state.

## Effort

XS — one-line change.

## v1.5 Priority

Medium. Do alongside any v1.5 work on `MainViewModel`.
