---
phase: 08
slug: documentation-hygiene-and-verification
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-17
---

# Phase 08 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 |
| **Config file** | `CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |
| **Quick run command** | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -p:Platform=x64` |
| **Full suite command** | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -p:Platform=x64` |
| **Estimated runtime** | ~15 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -p:Platform=x64`
- **After every plan wave:** Run `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -p:Platform=x64`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 15 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 08-01-01 | 01 | 1 | CTXW-05, SETT-05, EXPT-01/02/03, SETT-02, UPDT-01, UIPF-05 | grep | `grep -c "\[x\]" .planning/REQUIREMENTS.md` | ✅ | ⬜ pending |
| 08-01-02 | 01 | 1 | Phase 02 reqs | file-exists | `test -f .planning/phases/02-*/02-VERIFICATION.md` | ❌ W0 | ⬜ pending |
| 08-01-03 | 01 | 1 | Phase 04 reqs | file-exists | `test -f .planning/phases/04-*/04-VERIFICATION.md` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

Existing infrastructure covers all phase requirements. This phase makes no code changes — only documentation edits. No new test files required.

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| All 8 checkboxes marked [x] | All phase reqs | Documentation edit verification | Grep REQUIREMENTS.md for each ID, confirm [x] |
| VERIFICATION.md files have correct structure | Phase 02, 04 | Format check | Read files, confirm frontmatter + sections match template |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 15s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
