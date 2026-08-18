---
created: 2026-08-17T19:07:00Z
title: Move the Dark Mode toggle up, directly below Autostart
area: ui
files:
  - CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml
---

## Problem

In the Settings "General" card the Dark Mode toggle sits as **Row 4**, buried between the session
visibility window dropdown and the language dropdown:

1. Autostart (toggle) — `SettingsView.xaml:143`
2. Refresh interval (combo) — `:161`
3. Session timeout (combo) — `:180`
4. Session visibility window (combo) — `:200`  (commented "Row 3.5")
5. **Dark Mode (toggle)** — `:221`
6. Language (combo) — `:239`
7. Reset window size (button) — `:265`

The two toggles belong together at the top; a frequently-flipped appearance switch should not sit
below three dropdowns the user configures once.

## Solution

Move the Dark Mode `<Grid>` block (`SettingsView.xaml:220-234`, including the `<!-- Row 4: Dark
Mode -->` comment) so it directly follows the Autostart block that ends at `:156`. The rows are
siblings in a plain `<StackPanel>` (`:140`) — visual order is document order, and no `Grid.Row`
indices or bindings need touching.

Two things to get right while moving it:

- **Divider chain.** The rows are separated by `<Border Height="1" ... />` dividers. Removing the
  block from its old position leaves the dividers at `:218` and `:236` adjacent — drop one of them.
  Insert exactly one divider after the relocated block. Net divider count stays unchanged.
- **Renumber the comments.** `Row 1`…`Row 5` plus the odd `Row 3.5` become wrong the moment the
  order changes. Either renumber them or drop the numbers and keep the names.

## Dark Mode default is already `true` — verified, no change needed

The second half of the request is already implemented, at every step of the chain:

- `AppSettings.cs:29` — `DefaultColorMode = DarkColorMode`
- `AppSettings.cs:82` — `ColorMode { get; set; } = DefaultColorMode`, so a fresh settings.json is dark
- `SettingsService.cs:137-142` — an unsupported persisted value is coerced back to `DefaultColorMode`
- `App.xaml.cs:203` — `RequestedTheme` is Light only on an explicit `"light"`, Dark otherwise
- `SettingsViewModel.cs:487` — `_isDarkMode = settings.ColorMode != LightColorMode`, so the toggle
  renders ON

A fresh install therefore starts dark with the switch on. Only an explicit user choice of light mode
persists as light, which is the intended behaviour. If the app is ever observed starting in light
mode on a clean profile, that is a bug in this chain — not a missing default.
