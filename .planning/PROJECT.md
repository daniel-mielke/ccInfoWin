# ccInfo Windows

## What This Is

A Windows 11 desktop application for real-time monitoring of Claude Code usage limits. Port of the macOS app [ccInfo](https://github.com/stefanlange/ccInfo) (v1.7.1) by Stefan Lange, adapted for Windows with WinUI 3. Shipped as v1.0 with full feature parity across all 10 functional areas, v1.1 with UI polish matching the updated macOS reference design, and v1.2 bringing parity with macOS ccInfo v1.8.3.

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

### Active

## Current Milestone: v1.2 macOS v1.8.3 Feature Parity

**Goal:** Bring ccInfoWin to feature parity with macOS ccInfo v1.8.3 — model-based context detection, Sonnet context setting, session cleanup, stable subagent order, and footer accessibility.

**Target features:**
- 1M context window support with model-based detection (Opus=1M, Sonnet=configurable, Haiku=200K)
- Sonnet context window setting in Settings (200K/1M picker)
- Session filtering — hide sessions for deleted project directories
- Subagent sorting stabilization (alphabetical by agentId)
- Footer tooltip and accessibility enhancement (localized tooltips, AutomationProperties.Name)

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

Shipped v1.1 with UI polish across 3 phases (41 commits, ~1,470 net LOC changes from v1.0 baseline).
Full v1.1 stats: 3 phases, 6 plans, 10 tasks, 18/18 requirements satisfied.
Phase 12 complete — model-based context detection (Opus=1M, Haiku=200K, Sonnet=configurable default 200K, flat 33K buffer, 20K warning).
Tech stack: C# 13 / .NET 9 / WinUI 3 (Windows App SDK 1.8) / Win2D / WebView2 / CommunityToolkit.Mvvm 8.4.
Detailed upgrade spec available: `spec-release-from-1.7.1-to-1.8.3.md` (5 phases, dependency chain documented).

**Known tech debt:**
- 13 pre-existing unit test failures in JsonlServiceTests (parameter naming mismatch, production unaffected)
- STYLE-04 spec drift: CornerRadius=999 documented, CornerRadius=11 in live code (visually equivalent at 22px badge height)
- ExportHelper.cs line 245 hardcodes isDark:true for chart export (pre-existing, not a v1.1 regression)

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
*Last updated: 2026-04-12 after Phase 12 completion*
