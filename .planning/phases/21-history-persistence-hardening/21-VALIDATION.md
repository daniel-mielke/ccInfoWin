---
phase: 21
slug: history-persistence-hardening
status: draft
nyquist_compliant: false
wave_0_complete: true
created: 2026-05-06
---

# Phase 21 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 + Moq 4.20.72 (existing test project) |
| **Config file** | `CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |
| **Quick run command** | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~UsageHistory"` |
| **Full suite command** | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |
| **Estimated runtime** | ~5–15 seconds (file-system tests use temp paths + Guid namespacing per existing pattern) |

---

## Sampling Rate

- **After every task commit:** Run quick command (filtered to UsageHistory tests)
- **After every plan wave:** Run full suite
- **Before `/gsd-verify-work`:** Full suite must be green + manual smoke (HIST-01 termination + restart) executed
- **Max feedback latency:** 15 seconds

---

## Per-Task Verification Map

> Filled by planner during plan generation.

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| TBD     | TBD  | TBD  | TBD         | —          | TBD             | TBD       | TBD               | TBD         | ⬜ pending |

---

## Wave 0 Requirements

**No new test scaffolding needed** — existing `UsageHistoryServiceTests.cs` covers the test pattern (temp-path file I/O via `IDisposable.Dispose`). New tests for HIST-02, HIST-03, HIST-04, HIST-05 are added to the existing file (or a sibling `UsageHistoryHardeningTests.cs`).

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| App.X-button close persists post-poll history points | HIST-01 | `AppWindow.Closing` requires a real WinUI 3 host — not triggerable in headless xUnit | 1. Launch app; sign in; wait for usage data to load. 2. Inspect `%LOCALAPPDATA%\CCInfoWindows\history.json` size/mtime. 3. Trigger an in-memory append (e.g., wait for next poll cycle). 4. Click X to close (do NOT use Task Manager). 5. Restart app; confirm history.json contains the appended point. |
| 5-hour window reset clears chart cleanly without vertical cliff | HIST-04 (visual) | UI rendering verification — chart must visually reset without a stutter or stale-data flash | 1. Launch app with existing history. 2. Note current chart shape. 3. Wait for `ResetsAt` to advance (or simulate by editing the cached JSON). 4. Confirm next poll clears the chart immediately, no vertical cliff. |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or are explicitly manual (HIST-01)
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] No watch-mode flags
- [ ] Feedback latency < 15s
- [ ] Manual smoke checklist (HIST-01, HIST-04 visual) executed and recorded in VERIFICATION.md before phase verify
- [ ] `nyquist_compliant: true` set in frontmatter (after planner fills Per-Task Verification Map)

**Approval:** pending
