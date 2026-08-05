---
phase: 12
slug: model-based-context-detection
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-04-12
---

# Phase 12 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | MSTest / .NET 9 |
| **Config file** | `CCInfoWindows/CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |
| **Quick run command** | `dotnet test CCInfoWindows/CCInfoWindows.Tests --filter "ClassName~ModelContextLimits"` |
| **Full suite command** | `dotnet test CCInfoWindows/CCInfoWindows.Tests` |
| **Estimated runtime** | ~15 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test CCInfoWindows/CCInfoWindows.Tests --filter "ClassName~ModelContextLimits"`
- **After every plan wave:** Run `dotnet test CCInfoWindows/CCInfoWindows.Tests`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 15 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 12-01-01 | 01 | 1 | CTX-01 | unit | `dotnet test --filter "ClassName~ModelContextLimits"` | ❌ W0 | ⬜ pending |
| 12-01-02 | 01 | 1 | CTX-02 | unit | `dotnet test --filter "ClassName~ModelContextLimits"` | ❌ W0 | ⬜ pending |
| 12-01-03 | 01 | 1 | CTX-04 | unit | `dotnet test --filter "ClassName~ModelContextLimits"` | ❌ W0 | ⬜ pending |
| 12-01-04 | 01 | 1 | CTX-05 | unit | `dotnet test --filter "ClassName~ModelContextLimits"` | ❌ W0 | ⬜ pending |
| 12-01-05 | 01 | 1 | CTX-03 | unit | `dotnet test --filter "ClassName~ModelContextLimits"` | ❌ W0 | ⬜ pending |
| 12-01-06 | 01 | 1 | CTX-06 | integration | `dotnet build CCInfoWindows/CCInfoWindows.csproj` | ✅ | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] Update existing `ModelContextLimitsTests` to reflect new expected values (Opus=1M, flat 33K buffer, 20K warning)
- [ ] Add test cases for `GetModelFamily` enum resolution
- [ ] Add test cases for `GetMaxContextTokens` with sonnetContextSize parameter

*Existing test infrastructure covers framework setup — no new framework install needed.*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Context progress bar shows ~967K for Opus | CTX-01 | Visual UI verification | Launch app, open Opus session, verify context display shows ~967K |
| Autocompact warning appears at 20K remaining | CTX-03 | Requires live session near threshold | Monitor session approaching context limit |
| Subagent bars show correct model-based limits | CTX-05 | Visual UI verification | Open session with subagents, verify each bar shows model-appropriate limit |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 15s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
