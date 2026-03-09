# ccInfo Windows

## What This Is

A Windows 11 desktop application for real-time monitoring of Claude Code usage limits. It is a port of the macOS app [ccInfo](https://github.com/stefanlange/ccInfo) (v1.7.1) by Stefan Lange, adapted for Windows with three deliberate differences: opaque background (no vibrancy), persistent standalone window (no MenuBar popup), and no taskbar metric integration.

Target audience: Developers with active Claude Pro/Max subscriptions using Claude Code on Windows.

## Core Value

Developers can see their Claude usage limits (5-hour window, weekly quota, context window) at a glance in real-time, preventing unexpected throttling during active coding sessions.

## Requirements

### Validated

(None yet — ship to validate)

### Active

- [ ] Authentication via embedded WebView2 with secure token storage in Windows Credential Manager
- [ ] 5-hour usage window with interactive area chart (color-coded zones: green/yellow/orange/red)
- [ ] Weekly usage limit display with per-model breakdown (Sonnet/Opus)
- [ ] Context window status with main session + subagent progress bars and model badges
- [ ] Multi-session management with dropdown, activity threshold, and readable session names
- [ ] Token statistics aggregated by session/today/week/month with tab switcher
- [ ] Cost calculation with live LiteLLM pricing, tiered pricing support, and burn rate
- [ ] Chart export as PNG (file save + clipboard copy)
- [ ] Settings (refresh interval, autostart, session threshold, language DE/EN, dark/light mode)
- [ ] Auto-update check with in-app banner notification
- [ ] Dark mode and light mode with manual toggle (default: dark)
- [ ] Localization (German + English, follows system language)
- [ ] Open source release on GitHub with Inno Setup installer

### Out of Scope

- Taskbar/System Tray integration — all metrics visible in main window
- Configurable MenuBar slots — no Windows equivalent, not needed
- macOS-specific integrations (Keychain, FSEvents, Share Sheet) — replaced by Windows equivalents
- Separate settings window — settings displayed in-app (same window, frame navigation)
- Transparent/blur background — opaque background by design

## Context

### Reference Implementation

The macOS app [stefanlange/ccInfo](https://github.com/stefanlange/ccInfo) v1.7.1 serves as the functional and visual reference. Three detailed specification documents exist in the project root:

- `ccinfo-spec.md` — Full functional requirements (10 areas, 40+ requirements with FA-IDs)
- `ccinfo-tech-spec.md` — Technical specification (C#/WinUI 3/MVVM architecture, component details)
- `ccinfo-styleguide.md` — Pixel-precise design guide (colors, typography, layout, animations)

### Data Sources

1. **Claude.ai Web API** — 5-hour and weekly usage data via authenticated HTTP requests (session cookie)
2. **Claude Code JSONL files** — Local log files for session, token, and cost data (`%USERPROFILE%\.claude\projects\`)
3. **LiteLLM Pricing API** — Current model prices with 12-hour cache

### GitHub Repository

Repository: `https://github.com/daniel-mielke/ccInfoWin`
Visibility: Public
License: MIT

## Constraints

- **Tech stack**: C# 12+ / .NET 8 / WinUI 3 (Windows App SDK 1.5+) — as specified in tech spec
- **Platform**: Windows 10 (Build 19041) minimum, Windows 11 target
- **Performance**: < 50 MB RAM, < 1% CPU idle
- **UI framework**: WinUI 3 with Win2D for chart rendering, WebView2 for login
- **Packaging**: Unpackaged (no MSIX), Inno Setup EXE installer
- **No admin rights**: Must install and run without elevation
- **Design**: Must visually match macOS original (per styleguide) except documented deviations
- **Dev environment**: Visual Studio 2022 with WinUI 3 workload (needs setup)

## Security

### Credential Protection

- **Windows Credential Manager only** — Session tokens stored exclusively via Win32 `CredRead`/`CredWrite` (DPAPI-encrypted, bound to Windows user account). Never stored as plaintext on disk.
- **WebView2 isolation** — Separate process with dedicated user-data directory under `%LOCALAPPDATA%\CCInfoWindows\WebView2`. Not committed to source control.

### Source Code Security (Open Source)

- **No secrets in repository** — Zero hardcoded API keys, tokens, passwords, or credentials in source code
- **Comprehensive .gitignore from day one** — Must exclude: `bin/`, `obj/`, `.vs/`, `*.user`, `launchSettings.json`, any local config with paths or tokens
- **No telemetry** — Zero data collection, zero tracking
- **Network allowlist** — App communicates exclusively with `claude.ai` (HTTPS) and `raw.githubusercontent.com` (LiteLLM pricing + update check). No other network connections.
- **Local data in %LOCALAPPDATA%** — Settings, caches, and usage history stored in user-scoped directory, never in the repo or shared locations

### Pre-Commit Practices

- Code review for accidental credential exposure before every commit
- No example configs with real values — use placeholder patterns only
- `.gitignore` validated as first commit artifact

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Port of macOS ccInfo, not rewrite | Proven feature set, clear visual reference, known data sources | — Pending |
| C# / WinUI 3 stack | Modern Windows-native, MVVM standard, Win2D for charts, WebView2 built-in | — Pending |
| Opaque background instead of vibrancy | No reliable cross-version transparency on Windows, cleaner look | — Pending |
| Persistent window instead of popup | No Windows MenuBar equivalent, standalone window more natural on Windows | — Pending |
| Win32 CredRead/CredWrite over PasswordVault | PasswordVault has known issues in WinUI 3 full-trust apps | — Pending |
| Inno Setup over MSIX | Simpler distribution, no Store dependency, no admin needed for per-user install | — Pending |
| JSON storage over SQLite | Data volumes tiny (few KB), no relational queries, trivial with System.Text.Json | — Pending |
| Full v1 scope (all 10 areas) | Complete feature parity with macOS original is the goal | — Pending |

---
*Last updated: 2026-03-09 after initialization*
