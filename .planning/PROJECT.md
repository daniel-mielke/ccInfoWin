# ccInfo Windows

## What This Is

A Windows 11 desktop application for real-time monitoring of Claude Code usage limits. Port of the macOS app [ccInfo](https://github.com/stefanlange/ccInfo) by Stefan Lange, adapted for Windows with WinUI 3. Shipped as v1.0 (full feature parity, 10 functional areas), v1.1 (UI polish matching macOS reference), v1.2 (macOS v1.8.3 feature parity — model-based context detection, Sonnet context setting, session cleanup, footer accessibility), v1.3 (macOS v1.10.0 feature parity — burn rate warning, chart gradient, settings redesign), v1.4 (macOS v1.11.1 feature parity — auth flow stability, history persistence hardening, UI polish, localization gaps), and v1.5 (macOS v1.12.0 feature parity + hardening — dispatcher foundation with G-1/G-2/G-3 conventions, cold-start session hydration + visibility window, persistent session renaming, next-window label + org-id picker + pricing surfacing + L10N completion).

Target audience: Developers with active Claude Pro/Max subscriptions using Claude Code on Windows.

## Core Value

Developers can see their Claude usage limits (5-hour window, weekly quota, context window) at a glance in real-time, preventing unexpected throttling during active coding sessions.

## Requirements

### Validated

- ✓ Authentication via embedded WebView2 with secure token storage in Windows Credential Manager — v1.0
- ✓ 5-hour usage window with interactive area chart (color-coded zones: green/yellow/orange/red) — v1.0
- ✓ Weekly usage limit display with per-model breakdown (Sonnet/Opus) — v1.0
- ✓ Context window status with main session + subagent progress bars and model badges — v1.0
- ✓ Multi-session management with dropdown, activity threshold, and readable session names — v1.0
- ✓ Token statistics aggregated by session/today/week/month with tab switcher — v1.0
- ✓ Cost calculation with live LiteLLM pricing, tiered pricing support — v1.0
- ✓ Chart export as PNG (file save + clipboard copy) — v1.0
- ✓ Settings (refresh interval, autostart, session threshold, language DE/EN, dark/light mode) — v1.0
- ✓ Auto-update check with in-app banner notification — v1.0
- ✓ Dark mode and light mode with manual toggle (default: dark) — v1.0
- ✓ Localization (German + English, follows system language) — v1.0
- ✓ Open source release on GitHub with Inno Setup installer — v1.0
- ✓ Visually consistent layout: equal 16px padding, Active Session header, correct section order, scrollable footer, Statistics separator — v1.1
- ✓ Unified visual style: 6px progress bars with semi-transparent gray track, rounded ComboBox, pill model badges, matching chart axis colors, consistent Statistics label styling — v1.1
- ✓ Timer formatting: values ≥24h displayed as "Xd Yh" with localized units — v1.1
- ✓ Interaction polish: logout button red with icon, login button with icon, smooth refresh animation completing full 360° rotation before stopping — v1.1
- ✓ Model-based context detection: Opus=1M, Sonnet=configurable (200K/1M), Haiku=200K, flat 33K buffer, 20K warning threshold — v1.2
- ✓ Sonnet context window setting: 200K/1M ComboBox in Settings with immediate live refresh via messenger — v1.2
- ✓ Session orphan filtering: hide sessions for deleted project directories, UNC path guard, alphabetical subagent sort — v1.2
- ✓ Footer tooltip and accessibility: localized ToolTipService.ToolTip and AutomationProperties.Name on all footer buttons — v1.2
- ✓ Burn rate warning: prediction engine with linear regression over last 15 minutes, inline banner with flame icon, one-shot toast notification — v1.3
- ✓ Chart horizontal gradient: smooth green→yellow→orange→red transitions at 25% opacity, 100% line stroke, correct gap handling, theme-aware — v1.3
- ✓ Settings redesign: Segmented Control with General/Updates/Account/About tabs (colored badges) at 360px width — v1.3
- ✓ Session watcher verification: FileSystemWatcher confirmed to catch file-level .jsonl changes in subdirectories — v1.3
- ✓ Auth flow stability: `_autoReauthAttempted` state machine — first 401 auto-navigates to LoginView, second 401 falls back to InfoBar; `App.MainWindow.Activate()` before frame navigation; LoginView reload button with Slate-900 pill wrapper for cross-surface contrast — v1.4
- ✓ History persistence hardening: `IUsageHistoryService` async/sync variants with byte-identical output, `SemaphoreSlim` write guard, `MainWindow.OnClosing` synchronous snapshot flush, ResetsAt comparison clears Points on 5-hour reset — v1.4
- ✓ UI polish: refresh spinner anti-flicker with 250 ms `Task.Delay` floor and `IsEnabled` belt-and-suspenders override, inactive-session ComboBox tooltip recomputes via `SessionTimeoutChangedMessage`, About-tab `IDispatcherTimer` adapter with full lifecycle management — v1.4
- ✓ Localization gaps: 4 new resw keys (DE/EN) — `NotSignedIn.Text`, `NoData.Text`, `Loading.Text`, `InactiveSessionTooltip`; `ResourceCoverageTests` validates all 6 L10N-01 keys × 2 locales structurally — v1.4

### Active

## Current Milestone: v1.5 macOS v1.12.0 Feature Parity + Hardening

**Goal:** Bring CCInfoWindows to upstream v1.12.0 feature parity (next-window label + session renaming) while remediating v1.4 code-review findings and three reproducible cold-start / silent-failure bugs.

**Target features (3 clusters, ~11 items):**

**Cluster A — macOS v1.12.0 Feature Parity:**
- A1: Next 5h-window start time label below countdown (weekday + clock, e.g. "Mo 1.5. 16:30")
- A2: Session renaming via pencil button next to switcher + new "Sessions" Settings tab; custom names persist across restarts

**Cluster B — Bug Hardening:**
- B1: Session-dropdown empty on cold start — fragile `Cwd` hydration in `JsonlService` + add configurable session-visibility window (default 30 days; options 7 / 30 / 90 / unlimited)
- B2: Org-ID picker for multi-account users — detect zero-utilization heuristic + Settings UI override + force re-resolve
- B3: Pricing-service silent-failure — exception in `EnsurePricesLoadedAsync` surfaced via UI banner / `HasApiError` instead of swallowed `Debug.WriteLine`

**Cluster C — v1.4 Code-Review Remediation** (see `.planning/todos/pending/2026-05-07-*`):
- C-1 (critical): Fire-and-forget exception swallow in `MainViewModel.Receive(AuthStateChangedMessage)`
- C-2 (critical): Missing `DispatcherQueue` marshaling in `Receive(AuthStateChangedMessage)` — candidate for `IDispatcherQueue` adapter mirror of `IDispatcherTimer`
- M-1: Delete orphan `LogoutRequestedMessage.cs` (dead code from reverted Plan 21-03)
- M-2: Localize hardcoded EN strings in `LastFetchRelativeTime` — couples with B3
- M-3: Restore real default for `_contextModelBadgeColor = null!`
- Nits: 3 minor opportunistic cleanups (bundled)

**Key context:**
- Continuation of v1.4 = v1.11.1 parity narrative
- C-2 same architectural family as the v1.4 `WeakReferenceMessenger` pitfall — opportunity to standardize a thread-marshaling rule across all `IRecipient<>` declarations
- M-2 + B3 share the pricing-service surface (`SettingsViewModel.LastFetchRelativeTime`)
- Stack unchanged (C# 13 / .NET 9 / WinUI 3 / WinAppSDK 1.8)

### Future

- [ ] V2-01: System tray icon with quick status overview
- [ ] V2-02: Keyboard shortcuts for common actions
- [ ] V2-03: Configurable color thresholds for progress bars
- [ ] V2-04: Historical usage trends (daily/weekly graphs)
- [ ] V2-05: Migration to .NET 10 LTS when WinAppSDK confirms compatibility

### Out of Scope

- Taskbar/System Tray integration — all metrics visible in main window
- Configurable MenuBar slots — no Windows equivalent, not needed
- macOS-specific integrations (Keychain, FSEvents, Share Sheet) — replaced by Windows equivalents
- Separate settings window — settings displayed in-app (same window, frame navigation)
- Transparent/blur background — opaque background by design
- ML-based usage predictions — over-engineered for a desktop widget
- Multi-account support — adds auth/UI complexity, target audience is single developer
- Mobile companion app — Claude Code is a desktop tool
- JSON/CSV data export — niche feature, chart PNG export covers sharing use case
- SQLite database — overkill for few KB of data, JSON files sufficient

## Context

### Current State

Shipped v1.4 (macOS v1.11.1 feature parity) across 4 phases (51 commits, +11,115/-42 lines, 64 files changed).
Cumulative: 23 phases, 53 plans across 5 milestones (v1.0 → v1.1 → v1.2 → v1.3 → v1.4).
Tech stack: C# 13 / .NET 9 / WinUI 3 (Windows App SDK 1.8) / Win2D / WebView2 / CommunityToolkit.Mvvm 8.4.
Test surface: 26+ tests GREEN on v1.4-modified surface; 4 new test classes (`MainViewModelAuthFlowTests`, expanded `UsageHistoryServiceTests`, `SettingsViewModelTimerTests`, `ResourceCoverageTests`).

**Known tech debt (deferred to v1.5+):**
- WeakReferenceMessenger + AddTransient ViewModels = recipient-GC pitfall (caught during v1.4 logout hotfix; documented in `architecture_weakreferencemessenger_with_transient_vms.md`)
- Cold-start session scanning: `IJsonlService` doesn't surface sessions <120 min after fresh launch (blocks POLISH-04 visual smoke)
- Multi-account org-id picker: `TryMigrateOrgIdAsync` blindly takes `orgs[0]`
- Pricing service silent failure: `EnsurePricesLoadedAsync` exception swallowed by catch-all; About-tab shows "Never"
- 2 pre-existing test failures in ClaudeApiServiceTests (parameter naming mismatch, production unaffected — unchanged from v1.3 baseline)
- STYLE-04 spec drift: CornerRadius=999 documented, CornerRadius=11 in live code (visually equivalent at 22 px badge height)
- GetZoneSegments marked [Obsolete] in ChartRenderer.cs — 5 existing tests still reference it (v1.3)
- AUTH-01/02 visual smoke deferred — dev build can't easily force a 401 (full unit-test coverage compensates)

### Reference Implementation

The macOS app [stefanlange/ccInfo](https://github.com/stefanlange/ccInfo) v1.7.1 serves as the functional and visual reference. Three detailed specification documents exist in the project root:

- `ccinfo-spec.md` — Full functional requirements (10 areas, 40+ requirements with FA-IDs)
- `ccinfo-tech-spec.md` — Technical specification (C#/WinUI 3/MVVM architecture, component details)
- `ccinfo-styleguide.md` — Pixel-precise design guide (colors, typography, layout, animations)

### Data Sources

1. **Claude.ai Web API** — 5-hour and weekly usage data via WebView2 bridge (Cloudflare bypass)
2. **Claude Code JSONL files** — Local log files for session, token, and cost data (`%USERPROFILE%\.claude\projects\`)
3. **LiteLLM Pricing API** — Current model prices with 12-hour cache + bundled fallback

### GitHub Repository

Repository: `https://github.com/daniel-mielke/ccInfoWin`
Visibility: Public
License: MIT

## Constraints

- **Tech stack**: C# 13 / .NET 9 / WinUI 3 (Windows App SDK 1.8)
- **Platform**: Windows 10 (Build 19041) minimum, Windows 11 target
- **Performance**: < 50 MB RAM, < 1% CPU idle
- **UI framework**: WinUI 3 with Win2D for chart rendering, WebView2 for login
- **Packaging**: Unpackaged (no MSIX), Inno Setup EXE installer
- **No admin rights**: Must install and run without elevation
- **Design**: Must visually match macOS original (per styleguide) except documented deviations
- **API bypass**: Cloudflare bot protection requires WebView2 bridge pattern (not HttpClient)

## Security

### Credential Protection

- **Windows Credential Manager only** — Session tokens stored exclusively via Win32 `CredRead`/`CredWrite` (DPAPI-encrypted, bound to Windows user account). Never stored as plaintext on disk.
- **WebView2 isolation** — Separate process with dedicated user-data directory under `%LOCALAPPDATA%\CCInfoWindows\WebView2`. Not committed to source control.
- **Logout cleanup** — WebViewBridge.Reset() drains pending requests and releases CoreWebView2 reference on logout.

### Source Code Security (Open Source)

- **No secrets in repository** — Zero hardcoded API keys, tokens, passwords, or credentials in source code
- **Comprehensive .gitignore from day one** — Excludes: `bin/`, `obj/`, `.vs/`, `*.user`, `launchSettings.json`, any local config with paths or tokens
- **No telemetry** — Zero data collection, zero tracking
- **Network allowlist** — App communicates exclusively with `claude.ai`, `raw.githubusercontent.com`, and `api.github.com` (HTTPS only)
- **Local data in %LOCALAPPDATA%** — Settings, caches, and usage history stored in user-scoped directory

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Port of macOS ccInfo, not rewrite | Proven feature set, clear visual reference, known data sources | ✓ Good — achieved full parity |
| C# / WinUI 3 stack | Modern Windows-native, MVVM standard, Win2D for charts, WebView2 built-in | ✓ Good — stable and performant |
| WebView2 bridge for API calls | Cloudflare blocks .NET HttpClient TLS fingerprint | ✓ Good — solved 403 errors |
| Opaque background instead of vibrancy | No reliable cross-version transparency on Windows | ✓ Good — clean look |
| Persistent window instead of popup | No Windows MenuBar equivalent | ✓ Good — natural on Windows |
| Win32 CredRead/CredWrite over PasswordVault | PasswordVault has known issues in WinUI 3 full-trust apps | ✓ Good — reliable DPAPI |
| Inno Setup over MSIX | Simpler distribution, no Store dependency, no admin needed | ✓ Good — per-user install works |
| JSON storage over SQLite | Data volumes tiny (few KB), no relational queries | ✓ Good — trivial I/O |
| Full v1 scope (all 10 areas) | Complete feature parity with macOS original is the goal | ✓ Good — 67/67 requirements met |
| l:Uids.Uid for runtime localization | x:Uid only works at XAML load time, not runtime language switch | ✓ Good — DE/EN switch works |
| AppTheme.xaml for global theming (v1.1) | Single source of truth for visual styles; swap without touching view code | ✓ Good — all style changes via ResourceDictionary |
| CornerRadius=11 for model badges (v1.1) | CornerRadius=999 causes WinUI 3 pill rendering issues at 22px height | ⚠️ Revisit — spec says 999, live is 11; visually equivalent now |
| _stopOnComplete flag for refresh animation (v1.1) | WinUI 3 Storyboard must complete current rotation before Stop() — no built-in API | ✓ Good — smooth completion without snap |
| Footer into ScrollViewer (v1.1) | Fixed footer created dead space; macOS reference scrolls footer with content | ✓ Good — matches macOS behavior |
| ModelFamily enum over token heuristic (v1.2) | Token-count guessing was fragile; model name is authoritative | ✓ Good — clean switch, future-proof |
| Flat 20K autocompact warning (v1.2) | Percentage thresholds gave wildly different absolute values per model | ✓ Good — consistent UX across 200K and 1M |
| Optional settingsService in JsonlService (v1.2) | Default null preserves 13+ existing test constructors unchanged | ✓ Good — zero test breakage |
| UNC path guard before Directory.Exists (v1.2) | Windows Directory.Exists hangs on unreachable UNC servers | ✓ Good — prevents app freeze |
| Explicit ToolTipService.ToolTip in XAML (v1.2) | WinUI3Localizer Uid-only injection doesn't create tooltip UI at parse time | ✓ Good — discovered and fixed via UAT |
| `_autoReauthAttempted` single bool flag (v1.4) | Simpler than state machine; testable; one-shot semantics fit the "first 401 vs subsequent 401" routing | ✓ Good — 4/4 `MainViewModelAuthFlowTests` GREEN |
| Hybrid sync+async history persistence (v1.4) | `Window.Closed` has no async-completion guarantee — sync at termination, async during poll | ✓ Good — byte-identical output; no UI stutter |
| `IDispatcherTimer` adapter for About-tab timer (v1.4) | WinRT `DispatcherTimer` is impossible to fake in headless tests; adapter enables `FakeDispatcherTimer` | ✓ Good — 6 lifecycle tests GREEN |
| Direct DI call instead of WeakReferenceMessenger for logout (v1.4 hotfix) | `WeakReferenceMessenger` + `AddTransient<MainViewModel>` causes recipient GC; messages silently dropped | ⚠️ Critical lesson — never use cross-VM messaging with transient lifetime for exactly-once flows |
| Gap-closure as additional wave within parent phase (v1.4) | Inserting decimal phases breaks REQ-ID mapping; appending a wave keeps the requirement-to-phase table stable | ✓ Good — preserved phase numbering across 3 gap-closures (20-05, 21-03, 22-04) |
| Belt-and-suspenders `IsEnabled` x:Bind on RelayCommand buttons (v1.4) | `[RelayCommand]` `CanExecute` chain can break under XAML binding-priority quirks; explicit `IsEnabled` reinforces it | ✓ Good — fixed UAT Test 1 refresh-button regression |

## Evolution

This document evolves at phase transitions and milestone boundaries.

**After each phase transition** (via `/gsd:transition`):
1. Requirements invalidated? → Move to Out of Scope with reason
2. Requirements validated? → Move to Validated with phase reference
3. New requirements emerged? → Add to Active
4. Decisions to log? → Add to Key Decisions
5. "What This Is" still accurate? → Update if drifted

**After each milestone** (via `/gsd:complete-milestone`):
1. Full review of all sections
2. Core Value check — still the right priority?
3. Audit Out of Scope — reasons still valid?
4. Update Context with current state

---
*Last updated: 2026-05-07 — v1.5 milestone started (macOS v1.12.0 Feature Parity + Hardening)*
