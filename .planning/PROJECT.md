# ccInfo Windows

## What This Is

A Windows 11 desktop application for real-time monitoring of Claude Code usage limits. Port of the macOS app [ccInfo](https://github.com/stefanlange/ccInfo) (v1.7.1) by Stefan Lange, adapted for Windows with WinUI 3. Shipped as v1.0 with full feature parity across all 10 functional areas.

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

## Current Milestone: v1.1 UI Polish & UX Improvements

**Goal:** Refine the visual design and interaction patterns to match the updated macOS reference app, improving layout consistency, visual hierarchy, and interaction quality.

**Target features:**
- Layout restructuring (section order, padding, separators)
- Visual style unification (progress bars, badges, dropdown, tabs)
- Text/formatting improvements (timer format, statistics labels)
- Interaction polish (refresh animation, logout button styling)

### Active

- [ ] LAYOUT-01: Vertikales Padding der Haupt-App entspricht dem horizontalen Padding
- [ ] LAYOUT-02: Label „Aktive Sitzung" über dem Projekt-Dropdown (DE/EN lokalisiert)
- [ ] LAYOUT-03: Trennlinie unterhalb des Projekt-Dropdowns
- [ ] LAYOUT-04: Sektion „Kontextfenster" zwischen „Aktive Sitzung" und „5-Stunden-Fenster"
- [ ] LAYOUT-05: Footer nicht fixiert, am Ende des Scroll-Inhalts mit Trennlinie
- [ ] LAYOUT-06: Trennlinie zwischen „Modelle" und „Eingabe" in Statistiken
- [ ] STYLE-01: Alle ProgressBars 6 px Höhe
- [ ] STYLE-02: ProgressBar-Hintergrundfarbe rgba(128,128,128,0.45)
- [ ] STYLE-03: Dropdown und Tab-Leiste: gleiche Hintergrundfarbe + CornerRadius ≥ 8 px
- [ ] STYLE-04: Modell-Badges als Pill-Shape (CornerRadius=999)
- [ ] STYLE-05: Achsenbeschriftungen 5h-Chart in Timer-Text-Farbe
- [ ] TEXT-01: Timer ab ≥ 24h im Format „Xd Yh"
- [ ] TEXT-02: Labels „Gesamt" und „Kosten (API-Äqu.)" in gleicher Farbe wie andere Labels
- [ ] TEXT-03: Schriftstärke „Kosten (API-Äqu.)" wie „Cache-Lesen"
- [ ] TEXT-04: Abstand vor „Gesamt" angeglichen
- [ ] INTER-01: Abmelden-Button: rot, weiße Schrift, Logout-Icon
- [ ] INTER-02: Login-Button: Login-Icon
- [ ] INTER-03: Refresh-Icon 360°-Rotation, API-gesteuert

### Out of Scope

- Taskbar/System Tray integration — all metrics visible in main window
- Configurable MenuBar slots — no Windows equivalent, not needed
- macOS-specific integrations (Keychain, FSEvents, Share Sheet) — replaced by Windows equivalents
- Separate settings window — settings displayed in-app (same window, frame navigation)
- Transparent/blur background — opaque background by design

## Context

### Current State

Shipped v1.0 with 6,244 LOC (5,417 C# + 827 XAML) across 75 commits in 9 days.
Tech stack: C# 13 / .NET 9 / WinUI 3 (Windows App SDK 1.8) / Win2D / WebView2 / CommunityToolkit.Mvvm 8.4.
Known tech debt: 13 pre-existing unit test failures in JsonlServiceTests (parameter naming mismatch, production unaffected).

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

---
*Last updated: 2026-03-19 after v1.1 milestone start*
