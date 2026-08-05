---
phase: 14
slug: session-management-polish
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-04-12
---

# Phase 14 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | MSTest / .NET 9 |
| **Config file** | `CCInfoWindows/CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |
| **Quick run command** | `dotnet test CCInfoWindows/CCInfoWindows.Tests --filter "ClassName~JsonlService"` |
| **Full suite command** | `dotnet test CCInfoWindows/CCInfoWindows.Tests` |
| **Estimated runtime** | ~15 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj`
- **After every plan wave:** Run `dotnet test CCInfoWindows/CCInfoWindows.Tests`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 15 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 14-01-01 | 01 | 1 | SES-01 | unit | `dotnet test --filter "ClassName~JsonlService"` | ❌ W0 | ⬜ pending |
| 14-01-02 | 01 | 1 | SES-02 | integration | `dotnet build` | ✅ | ⬜ pending |
| 14-01-03 | 01 | 1 | SES-03 | unit | `dotnet test --filter "ClassName~JsonlService"` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] Add `IsValidProjectDirectory` unit tests to `JsonlServiceTests.cs`
- [ ] Add `BuildSubagentContext` ordering test to verify alphabetical sort

*Existing test infrastructure covers framework setup — no new framework install needed.*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Session dropdown hides deleted projects | SES-01 | Requires running app + deleting a directory | Delete a project dir → refresh → verify session gone |
| Session resets on active dir deletion | SES-02 | Requires running app with active session | Delete active session's dir → verify auto-select to next |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 15s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
