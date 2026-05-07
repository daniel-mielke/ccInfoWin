---
phase: 20
slug: auth-flow-stability
status: draft
shadcn_initialized: false
preset: none
created: 2026-05-06
---

# Phase 20 — UI Design Contract: Auth Flow Stability

> Visual and interaction contract for Phase 20. The phase is **plumbing-heavy / UI-light**: the only visible delta is a single icon button on `LoginView` plus localized strings. Everything else (auto-reauth routing, post-login refresh, post-logout WebView2 navigation, background-window activation) is invisible behavior. This contract therefore reuses the **existing locked design system** in full and only declares the few specific tokens the new control consumes.

---

## Scope of Visible UI Delta

| Element | Type | Status |
|---------|------|--------|
| LoginView reload button (top-right overlay over WebView2) | New control | Adds 1 button |
| LoginView WebView2 visibility gate (Collapsed → Visible after login URL loaded) | Existing element, new visibility binding | No new visual |
| LoginView loading overlay (`ProgressRing`) | Existing element, extended display window | No visual change |
| MainView, SettingsView, footer, charts | — | **Untouched** |

**No new sections, no new colors, no new typography ramp, no new spacing tokens.** This phase consumes the locked design system from `spec/v1.7.1-macOS/ccinfo-styleguide.md`.

---

## Design System

| Property | Value |
|----------|-------|
| Tool | none (manual, locked design system) |
| Preset | not applicable (WinUI 3 / XAML, not shadcn) |
| Component library | WinUI 3 (Windows App SDK 1.8) — `Button`, `FontIcon`, `WebView2`, `ProgressRing`, `Grid` |
| Icon library | **Segoe Fluent Icons** (system font, already in use) |
| Font | **Segoe UI Variable** (Windows 11 system font, already in use) |
| Localization | WinUI3Localizer via `l:Uids.Uid` bindings against `Strings/{de-DE,en-US}/Resources.resw` |
| Theme resources | App.xaml `ApplicationPageBackgroundThemeBrush`, `SecondaryTextBrush` (already defined) |

Source: `spec/v1.7.1-macOS/ccinfo-styleguide.md`, `CCInfoWindows/CCInfoWindows/Views/MainView.xaml`.

---

## Spacing Scale

Phase 20 uses the **existing project spacing scale** (multiples of 4) — no exceptions.

| Token | Value | Usage in this phase |
|-------|-------|---------------------|
| 4px | xs | (not used) |
| 6px | — | `CornerRadius` of icon-button (matches MainView footer buttons; not on the 4-multiple grid but is the existing system value — **carry-over, not a new exception**) |
| 8px | sm | Reload-button `Margin="8"` (top + right offset from WebView2 edge) — matches the 8px `Margin` already used by the LoginView `InfoBar` |
| 8px | sm | Reload-button `Padding="8"` (matches MainView footer `Refresh`/`Settings`/`Quit` buttons) |
| 16px | md | (not used in this phase) |
| 24px | lg | (not used in this phase) |

**Exceptions:** none. The 6px `CornerRadius` is project-pre-existing (MainView footer buttons), not a new token.

**Note on CONTEXT D-05 vs. spec FEAT-11:** The phase-context document mentions `Padding="6"` and `FontSize=14` (copied from spec FEAT-11 sample code). The locked **visual coherence target is the MainView footer refresh button**, which uses `Padding="8"`, `FontSize=16`, `CornerRadius="6"`. **This UI-SPEC locks the MainView values** — visual coherence with the existing footer refresh button is the explicit goal in CONTEXT §Specifics.

---

## Typography

Phase 20 introduces **no text rendering** — the reload button is icon-only. Tooltip and AutomationProperties.Name use system defaults rendered by WinUI 3 (Segoe UI Variable, body weight, system size). No new role.

| Role | Size | Weight | Line Height | Used in Phase 20 |
|------|------|--------|-------------|------------------|
| System tooltip (WinUI 3 default) | 12px | Regular (400) | system | Yes — reload button tooltip |
| (no other text introduced by this phase) | — | — | — | — |

The locked styleguide ramp (11 / 12 / 13 / 14 / 28 px, weights Regular 400 / Medium 500 / Semibold 600 / Bold 700) remains untouched. Source: `spec/v1.7.1-macOS/ccinfo-styleguide.md` §3.2.

---

## Color

Phase 20 introduces **no new color**. The reload button reuses the existing icon-button color contract from MainView footer.

| Role | Value | Usage |
|------|-------|-------|
| Dominant (60%) — App background | `#1E1E1E` (Dark) / `#F5F5F5` (Light) | LoginView page background; loading overlay uses `ApplicationPageBackgroundThemeBrush` which resolves to these |
| Secondary (30%) — Surface tint | `#2C2C2E` (Dark) / `#EBEBF0` (Light) | not used by this phase |
| Accent (10%) — Action accent | `#007AFF` (system blue) | **NOT used by the reload button** — accent is reserved per styleguide §5 for dropdown chevrons and similar primary controls |
| Icon foreground (reload button glyph) | `SecondaryTextBrush` → `#8E8E93` (Dark) / `#6E6E73` (Light) | Glyph fill — matches MainView footer refresh icon |
| Button background | `Transparent` | Inherits page background; matches MainView footer pattern |
| Button hover background | `#3A3A3A` (Dark) / `#E8E8ED` (Light) | WinUI 3 default `Button` hover from theme — not explicitly redefined |

**Accent reserved for:** dropdown chevrons (`SessionComboBox`), `InfoBar.ActionButton` (`Re-Login` button), and any future primary CTAs. **The reload button is intentionally tertiary** — it is a recovery affordance, not a CTA, so it stays in the muted `SecondaryTextBrush` palette.

**Destructive color:** none introduced. Phase 20 has no destructive actions in its visible UI delta (logout already exists in SettingsView and is governed by Phase 18's SettingsRedesign contract — not in this phase's scope).

Source: `spec/v1.7.1-macOS/ccinfo-styleguide.md` §1.2, §3.2, §10.

---

## Component Inventory

### New: LoginView Reload Button

| Property | Value | Source |
|----------|-------|--------|
| Container | `Button` overlay child of root `Grid` (sibling of `LoginWebView`, declared **after** WebView2 so Z-order floats on top) | CONTEXT D-04 |
| Alignment | `HorizontalAlignment="Right"`, `VerticalAlignment="Top"` | CONTEXT D-04 |
| Margin | `8` (uniform) | CONTEXT D-04 |
| Padding | `8` (matches MainView footer button) | UI-SPEC decision (overrides CONTEXT D-05's `6`, see Spacing note above) |
| CornerRadius | `6` (matches MainView footer button) | MainView convention |
| Background | `Transparent` | CONTEXT D-05, MainView convention |
| BorderThickness | `0` | CONTEXT D-05, MainView convention |
| Glyph (FontIcon) | `&#xE72C;` (Segoe Fluent Icons "Refresh") | Spec FEAT-11 explicit |
| Glyph FontSize | `16` (matches MainView footer) | UI-SPEC decision |
| Glyph Foreground | `{ThemeResource SecondaryTextBrush}` | MainView convention |
| Tooltip | `l:Uids.Uid="LoginReloadButton"` → resolves `LoginReloadButton.Tooltip` and `LoginReloadButton.AutomationName` resw entries | CONTEXT D-05, Spec FEAT-16 |
| Click handler | `OnReloadLoginClicked` in code-behind → `LoginWebView?.CoreWebView2?.Reload();` (double null guard) | CONTEXT D-06, Spec FEAT-11b |
| Focus visual | WinUI 3 default keyboard focus ring | system |
| Hit-target | min 32×32 px (button bounding box at Padding=8, Glyph 16px) — meets WinUI 3 minimum | derived |

**Glyph rationale:** `&#xE72C;` is the Reload glyph (semantically "navigate-reload"). The MainView footer uses `&#xE895;` (ArrowSync, semantically "refresh data"). Distinct semantics → distinct glyphs is intentional and correct: the LoginView button reloads the **page**, not the data. Both glyphs come from the same Segoe Fluent Icons system font and render at the same visual weight.

### New: LoginView WebView2 Visibility Binding

| Property | Value | Source |
|----------|-------|--------|
| Element | `WebView2 x:Name="LoginWebView"` (existing) | LoginView.xaml line 12 |
| Initial state | `Visibility="Collapsed"` (NEW) | CONTEXT D-07 |
| Reveal trigger | `NavigationCompleted` where `args.IsSuccess == true` AND `CoreWebView2.Source` starts with `https://claude.ai/login` | CONTEXT D-08 |
| Bound to | Existing `LoginViewModel.IsLoading` property (semantics extended — stays `true` until login URL NavigationCompleted), bound via `BoolToVisibilityConverter` (inverted) — implementation detail at planner's discretion (CONTEXT §Claude's Discretion) | CONTEXT D-08 |
| Loading overlay | Existing `Grid` with `ProgressRing` + `ApplicationPageBackgroundThemeBrush` (LoginView.xaml lines 18-27) — **no visual change**, only an extended display window | LoginView.xaml |

**User-perceived behavior:** When the user logs out, they see the same loading overlay they see at app start. They never see the previous chat URL flash through. When the login URL has finished loading, the overlay fades and the login form appears. No second visibility flag is added — `IsLoading` is the single source of truth.

### Untouched (must not regress)

| Element | Constraint |
|---------|------------|
| `LoginView` `InfoBar` (top, error message) | Existing — must continue rendering at `VerticalAlignment="Top"`, `Margin="8"`. The new reload button is at `VerticalAlignment="Top"`, `HorizontalAlignment="Right"`, `Margin="8"` — they share the top-right corner. **Verify visually no overlap** when both are visible (rare: WebView2 init failure showing `ErrorMessage`). If overlap occurs, reload button takes precedence (ErrorMessage InfoBar can shift down or stretch to a non-conflicting region). |
| `MainView` footer refresh button | The reload button on LoginView **mirrors** this control's visual style. Any change to the MainView footer button would break visual coherence — out of scope for this phase. |
| `MainView` `SessionExpiredInfoBar` | The InfoBar fallback (second-401 path) is **unchanged** in markup or copy. Only the routing logic before it is changed (D-01). |

---

## Copywriting Contract

All copy is governed by the existing localization keys plus the two new keys `LoginReloadButton.Tooltip` and `LoginReloadButton.AutomationName`. **Phase 23 owns authoring** of these resw entries — Phase 20 references the keys via `l:Uids.Uid` and the keys must exist in both `Strings/de-DE/Resources.resw` and `Strings/en-US/Resources.resw` by the time the executor builds.

| Element | English (en-US) | German (de-DE) | Source |
|---------|-----------------|----------------|--------|
| Reload button tooltip | `Reload page` | `Seite neu laden` | Spec FEAT-16 §`LoginReloadButton.Tooltip` |
| Reload button accessibility name | `Reload login page` | `Login-Seite neu laden` | Spec FEAT-16 §`LoginReloadButton.AutomationName` |
| Session-expired InfoBar (unchanged, second-401 fallback) | `Your session has expired. Please re-login to continue.` | `Ihre Sitzung ist abgelaufen. Bitte melden Sie sich erneut an.` | Resources.resw existing key `SessionExpiredInfoBar.Message` |
| Re-Login button (unchanged, InfoBar action) | `Re-Login` | `Erneut anmelden` | Resources.resw existing key `ReLoginButton.Text` |

| State | Visible UI | Copy |
|-------|------------|------|
| First 401 in session | LoginView appears (auto-navigate); WebView2 hidden, loading overlay visible | (no new text — existing LoginView UI) |
| Second 401 in session | MainView remains; existing `SessionExpiredInfoBar` opens | Existing copy `SessionExpiredInfoBar.Title` / `.Message` + `ReLoginButton.Text` (unchanged) |
| Post-login | MainView refreshes immediately | (no new text) |
| Post-logout | LoginView appears, WebView2 hidden, loading overlay until login URL loads | (no new text) |
| Reload button click | Page reloads in-place; no toast/snackbar | (no new text — silent action) |
| Reload button click before WebView2 init | No-op (null-guarded), no error | (no error UI — defensive silence is correct) |
| Login URL fails to load (offline) | WebView2 stays Collapsed, loading overlay stays visible, reload button stays clickable | No new error copy. Existing `LoginView.InfoBar.ErrorMessage` may surface separately if `LoginViewModel` populates `ErrorMessage`. Reload button is the user's recovery path. |

**No primary CTA introduced by this phase.** The Re-Login InfoBar button (existing) is the only CTA in the visible UI delta and its copy does not change.

**No destructive actions introduced by this phase.** Logout already exists in SettingsView (governed by Phase 18) and is not modified by Phase 20 except for `_autoReauthAttempted = false` reset, which is invisible to the user.

**No empty state introduced by this phase.** The "WebView2 not loaded yet" state is covered by the existing loading overlay — no copy change.

---

## Interaction Contract

### State Machine: LoginView Visibility

```
[App start / Logout]
        │
        ▼
  IsLoading = true
  WebView2.Visibility = Collapsed
  Loading overlay visible
  Reload button visible (clickable, but no-op until WebView2 inits)
        │
        ▼ (NavigationCompleted fires, args.IsSuccess == true,
        │  Source starts with "https://claude.ai/login")
        ▼
  IsLoading = false
  WebView2.Visibility = Visible
  Loading overlay hidden
  Reload button visible (now functional — CoreWebView2 non-null)
        │
        ▼ (User clicks reload button)
        ▼
  CoreWebView2.Reload() invoked
  IsLoading semantics: implementation may flip back to true or stay false
  (planner discretion — CONTEXT §Claude's Discretion)
        │
        ▼ (NavigationCompleted re-fires for the reloaded URL)
        ▼
  Stable state — no further visibility change
```

### State Machine: 401 Routing

```
HTTP 401 received in WebViewBridge
        │
        ▼ AuthStateChangedMessage(false) sent
        ▼
MainViewModel.Receive(AuthStateChangedMessage):
        │
        ├── _autoReauthAttempted == false:
        │       _autoReauthAttempted = true
        │       NavigationService.NavigateTo<LoginView>()
        │       (IsSessionExpired stays false — InfoBar does NOT show)
        │
        └── _autoReauthAttempted == true:
                IsSessionExpired = true
                (Existing SessionExpiredInfoBar shows — Re-Login button visible)

HTTP 200 received OR Logout invoked OR AuthStateChangedMessage(true) received
        │
        ▼
  _autoReauthAttempted = false  (resets the auto-reauth budget)
```

### Background-Window Activation

Every `NavigationService.NavigateTo<TPage>()` call invokes `App.MainWindow?.Activate()` **before** `_frame.Navigate(...)`. Visible effect: if the user has minimized the window when a poll-cycle 401 fires, the window comes to the foreground showing LoginView immediately. No new UI element is introduced — this is a windowing-system behavior change.

---

## Accessibility

| Concern | Specification |
|---------|---------------|
| Keyboard focus | Reload button is a standard `Button` — receives keyboard focus, activates on Enter/Space. Default WinUI 3 focus ring renders. |
| Screen reader (Narrator) | `AutomationProperties.Name` from `LoginReloadButton.AutomationName` resw key. Reads as "Reload login page" / "Login-Seite neu laden". |
| Tooltip | `ToolTipService.ToolTip` from `LoginReloadButton.Tooltip` resw key. Visible on hover and on keyboard-focus per WinUI 3 default behavior. |
| Hit target | ≥32×32 px (Padding=8, FontSize=16 → bounding box ≈32×32) — meets WinUI 3 minimum touch target. |
| Color contrast | `SecondaryTextBrush` (#8E8E93 on #1E1E1E Dark; #6E6E73 on #F5F5F5 Light) — same as MainView footer icons; project-pre-existing acceptance. WCAG AA for icons (3:1) is met by both pairings. |
| Reduced motion | No animation introduced. The reload action is a synchronous WebView2 reload — page transition is the standard WebView2 navigation, no app-side animation. |
| Localization runtime switch | Tooltip and AutomationName must update when the user switches language without app restart (L10N-03 — verified by Phase 23 acceptance). |

---

## Registry Safety

| Registry | Blocks Used | Safety Gate |
|----------|-------------|-------------|
| (no registry) | not applicable — WinUI 3 / XAML project, no shadcn or third-party UI registry | not required |

This is a Windows desktop / C# / XAML project. The shadcn registry model does not apply. All controls (`Button`, `FontIcon`, `WebView2`, `ProgressRing`, `Grid`) come from the locked Microsoft.UI.Xaml component set shipped with Windows App SDK 1.8 — already vetted by project baseline.

---

## Locked Decisions Reference

| Decision | Source | UI-SPEC line |
|----------|--------|--------------|
| Reload button placement: top-right overlay over WebView2 | CONTEXT D-04 | Component Inventory §New: LoginView Reload Button |
| Reload button visual matches MainView footer refresh button | CONTEXT §Specifics, MainView.xaml lines 605-618 | Component Inventory + Color §Icon foreground |
| Reload button glyph: `&#xE72C;` (Segoe Fluent Reload) | Spec FEAT-11 | Component Inventory |
| Reload click: `LoginWebView?.CoreWebView2?.Reload()` with double null guard | CONTEXT D-06 | Component Inventory + Interaction Contract |
| WebView2 starts `Visibility="Collapsed"`, reveals only after login URL `NavigationCompleted IsSuccess` | CONTEXT D-07/D-08 | Component Inventory §New: LoginView WebView2 Visibility Binding |
| Localization keys `LoginReloadButton.Tooltip` / `.AutomationName` (DE + EN) — authored by Phase 23, referenced by Phase 20 | Spec FEAT-16, CONTEXT D-05 | Copywriting Contract |
| `App.MainWindow?.Activate()` before every `NavigateTo` | CONTEXT D-09 | Interaction Contract §Background-Window Activation |
| First 401 → auto-navigate, second 401 → existing InfoBar | CONTEXT D-01 | Interaction Contract §State Machine: 401 Routing |
| Post-login refresh: `Receive(AuthStateChangedMessage(true))` calls `RefreshUsageCommand.ExecuteAsync(null)` | CONTEXT D-03 | Interaction Contract §State Machine: 401 Routing |

---

## Diff Summary (for executor)

**Files visible UI delta touches:**

1. `CCInfoWindows/CCInfoWindows/Views/LoginView.xaml` — add reload `Button` as Grid child after WebView2; add `Visibility="Collapsed"` to `LoginWebView`; bind WebView2 visibility to inverse of `IsLoading`
2. `CCInfoWindows/CCInfoWindows/Views/LoginView.xaml.cs` — add `OnReloadLoginClicked` handler with double null guard
3. `CCInfoWindows/CCInfoWindows/ViewModels/LoginViewModel.cs` — extend `HandleNavigationCompleted` to keep `IsLoading=true` until login URL `NavigationCompleted` fires successfully
4. (Backend / non-UI: `MainViewModel`, `NavigationService` — out of UI-SPEC scope)

**Files this phase must NOT modify:**

- `Views/MainView.xaml` (footer is the visual reference, not a target)
- `Views/SettingsView.xaml`
- `App.xaml` theme resources
- Any chart, bar, or stats rendering

**Files Phase 20 references but Phase 23 authors:**

- `Strings/de-DE/Resources.resw` — must contain `LoginReloadButton.Tooltip` = `Seite neu laden` and `LoginReloadButton.AutomationName` = `Login-Seite neu laden` before Phase 20 ships, OR Phase 20 ships first with placeholder `ToolTipService.ToolTip="Reload"` strings and Phase 23 swaps in the resw bindings
- `Strings/en-US/Resources.resw` — must contain `LoginReloadButton.Tooltip` = `Reload page` and `LoginReloadButton.AutomationName` = `Reload login page`

---

## Checker Sign-Off

- [ ] Dimension 1 Copywriting: PASS (no new copy in this phase; reuses existing keys + 2 keys authored by Phase 23 with locked DE/EN values from Spec FEAT-16)
- [ ] Dimension 2 Visuals: PASS (new component mirrors locked MainView footer button; no new visual primitives)
- [ ] Dimension 3 Color: PASS (zero new colors; consumes `SecondaryTextBrush` and `Transparent` only)
- [ ] Dimension 4 Typography: PASS (zero new text rendered; system tooltip uses defaults)
- [ ] Dimension 5 Spacing: PASS (8px Margin + 8px Padding + 6px CornerRadius — all on existing project grid; carry-over from MainView footer)
- [ ] Dimension 6 Registry Safety: PASS (not applicable — WinUI 3 native components only)

**Approval:** pending
