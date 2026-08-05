---
phase: 4
slug: local-data-pipeline
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-11
---

# Phase 4 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xunit 2.9.3 + Moq 4.20.72 |
| **Config file** | `CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |
| **Quick run command** | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "Category!=Integration" -p:Platform=x64` |
| **Full suite command** | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -p:Platform=x64` |
| **Estimated runtime** | ~15 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "JsonlService|ContextWindow|TokenAggregation|SessionInfo" -p:Platform=x64`
- **After every plan wave:** Run `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -p:Platform=x64`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 15 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 04-01-01 | 01 | 0 | DATA-03 | unit | `dotnet test --filter "JsonlService"` | ❌ W0 | ⬜ pending |
| 04-01-02 | 01 | 0 | CTXW-01, CTXW-02, CTXW-04 | unit | `dotnet test --filter "ContextWindow"` | ❌ W0 | ⬜ pending |
| 04-01-03 | 01 | 0 | TOKS-01 | unit | `dotnet test --filter "TokenAggregation|TokenFormatter"` | ❌ W0 | ⬜ pending |
| 04-01-04 | 01 | 0 | SESS-01, SESS-05 | unit | `dotnet test --filter "SessionInfo"` | ❌ W0 | ⬜ pending |
| 04-XX-XX | TBD | TBD | DATA-04 | unit | `dotnet test --filter "FileWatcher"` | ❌ W0 | ⬜ pending |
| 04-XX-XX | TBD | TBD | SETT-03 | unit | `dotnet test --filter "SettingsService"` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `CCInfoWindows.Tests/Services/JsonlServiceTests.cs` — stubs for DATA-03, DATA-04, TOKS-01
- [ ] `CCInfoWindows.Tests/Helpers/ContextWindowTests.cs` — stubs for CTXW-01, CTXW-02, CTXW-04
- [ ] `CCInfoWindows.Tests/Helpers/TokenFormatterTests.cs` — stubs for TOKS-01 formatting
- [ ] `CCInfoWindows.Tests/Models/SessionInfoTests.cs` — stubs for SESS-01, SESS-05

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Session dropdown lists active sessions | SESS-02, SESS-03 | UI interaction | Open app with 2+ active CC sessions, verify dropdown populates |
| Switching sessions updates context bars | SESS-04 | UI interaction | Select different session, verify context window updates |
| Subagent context bars display | CTXW-03 | UI + live subagent | Run CC with subagent, verify separate bars appear |
| Autocompact warning appearance | CTXW-05 | UI visual | Fill context to >= 95%, verify warning badge/indicator |
| FileSystemWatcher live updates | DATA-04, DATA-05 | Requires live CC session | Run CC alongside app, verify data updates within debounce window |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 15s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
