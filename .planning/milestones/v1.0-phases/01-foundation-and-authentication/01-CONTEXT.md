# Phase 1: Foundation and Authentication - Context

**Gathered:** 2026-03-09
**Status:** Ready for planning

<domain>
## Phase Boundary

Project scaffold with WinUI 3/MVVM architecture, navigation shell (Main/Login/Settings frames), WebView2-based claude.ai login, secure credential storage via Windows Credential Manager, security baseline, CLAUDE.md, and specialized agent definitions. This phase delivers a launchable app that can authenticate — no data display yet.

</domain>

<decisions>
## Implementation Decisions

### Window Shell
- Standard Windows title bar (no custom title bar)
- Normal minimize to taskbar (no System Tray)
- X button = app exits completely
- Always-on-top: No (default), option for Settings in future phase
- Initial window size: 360px wide × 900px tall
- Window is freely resizable and maximizable
- Minimum window size: 300×500px
- Window position AND size saved on close, restored on startup

### Login Flow
- WebView2 fills the entire app window (full-window login view)
- 2FA/Captcha handled natively by WebView2 — no special app-side handling needed
- Login success detected via Cookie-Check: look for `sessionKey` cookie after navigation
- Token expiry during use (HTTP 401): show InfoBar banner with "Session expired" + Re-Login button (not invasive, not auto-redirect)
- WebView2 User Data Folder explicitly set to `%LOCALAPPDATA%\CCInfoWindows\WebView2`

### Project Infrastructure (CLAUDE.md)
- Create `CLAUDE.md` in project root with:
  - Info that this is a modified port of [stefanlange/ccInfo](https://github.com/stefanlange/ccInfo) (macOS v1.7.1)
  - Stack + conventions (C#/.NET 9/WinUI 3, MVVM, CommunityToolkit.Mvvm, naming, async patterns)
  - Project structure (Models/, Views/, ViewModels/, Services/, Helpers/, Converters/)
  - Build commands (dotnet build, dotnet run, dotnet publish)
  - Security rules (no secrets in code, Credential Manager only, .gitignore rules)
  - References to spec files with summaries:
    - `ccinfo-spec.md` — Functional requirements (10 areas, 40+ FA-IDs)
    - `ccinfo-tech-spec.md` — Technical specification (architecture, components)
    - `ccinfo-styleguide.md` — Pixel-precise design guide (colors, typography, layout)
  - References to coding guidelines: `.claude/DOS-Secure-Coding.pdf`, `.claude/DOS-Clean-code.pdf`

### Specialized Agents (`.claude/agents/`)
- `fullstack-dev.md` — Knows MVVM + WinUI 3 patterns, Win2D chart rendering, WebView2 integration, styleguide awareness (colors, fonts, spacing from ccinfo-styleguide.md)
- `code-review.md` — Checks: security (no secrets, Credential Manager), threading (DispatcherQueue for UI, async/await), MVVM compliance (no code-behind logic), memory/performance (Win2D leaks, FSW debouncing, HttpClient singleton). Knows `.claude/DOS-Secure-Coding.pdf` and `.claude/DOS-Clean-code.pdf` as authoritative guidelines.
- `git-agent.md` — Conventional Commits (feat:, fix:, chore:, docs:), branch strategy, PR creation with templates, .gitignore maintenance (prevent secret/build artifact commits)

### Claude's Discretion
- DI container setup and service registration pattern
- Exact navigation service implementation (Frame-based)
- WebView2 initialization retry strategy (corrupted UDF handling)
- Cookie extraction timing and validation approach
- .gitignore exact entries beyond the baseline

</decisions>

<specifics>
## Specific Ideas

- Window behavior should feel like a normal Windows desktop app — standard title bar, standard minimize/maximize/close, freely resizable
- Login should be seamless — the WebView2 IS the login, user interacts directly with claude.ai
- Token expiry should be non-invasive — banner notification, not a forced redirect that interrupts viewing cached data
- Coding guidelines PDFs (DOS-Secure-Coding.pdf, DOS-Clean-code.pdf) are authoritative references for code quality

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- None (greenfield project)

### Established Patterns
- None yet — Phase 1 establishes the patterns all subsequent phases will follow

### Integration Points
- Spec files in project root: `ccinfo-spec.md`, `ccinfo-tech-spec.md`, `ccinfo-styleguide.md`
- Coding guidelines: `.claude/DOS-Secure-Coding.pdf`, `.claude/DOS-Clean-code.pdf`
- Research findings: `.planning/research/` (STACK.md, ARCHITECTURE.md, PITFALLS.md especially relevant)

</code_context>

<deferred>
## Deferred Ideas

- Always-on-top toggle in Settings — Phase 6 (SETT category)
- System Tray minimize option in Settings — potential v2 feature

</deferred>

---

*Phase: 01-foundation-and-authentication*
*Context gathered: 2026-03-09*
