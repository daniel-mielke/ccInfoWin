---
phase: 9
slug: layout-structure
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-19
---

# Phase 9 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | dotnet test (xUnit) |
| **Config file** | CCInfoWindows/CCInfoWindows.Tests/CCInfoWindows.Tests.csproj |
| **Quick run command** | `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` |
| **Full suite command** | `dotnet test CCInfoWindows/CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |
| **Estimated runtime** | ~15 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj`
- **After every plan wave:** Run `dotnet test CCInfoWindows/CCInfoWindows.Tests/CCInfoWindows.Tests.csproj`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 15 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 9-01-01 | 01 | 1 | LAYOUT-01 | build | `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` | ✅ | ⬜ pending |
| 9-01-02 | 01 | 1 | LAYOUT-02 | build | `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` | ✅ | ⬜ pending |
| 9-01-03 | 01 | 1 | LAYOUT-03 | build | `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` | ✅ | ⬜ pending |
| 9-01-04 | 01 | 1 | LAYOUT-04 | build | `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` | ✅ | ⬜ pending |
| 9-01-05 | 01 | 1 | LAYOUT-05 | build | `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` | ✅ | ⬜ pending |
| 9-01-06 | 01 | 1 | LAYOUT-06 | build | `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` | ✅ | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

Existing infrastructure covers all phase requirements. This phase is pure XAML restructuring — no new test files needed. The build compiles XAML at build time, catching any structural errors automatically.

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Equal padding on all 4 sides | LAYOUT-01 | Visual check only | Launch app, verify top/bottom padding matches left/right visually |
| "Active Session" label above dropdown | LAYOUT-02 | Visual check | Verify label text is localized (DE: "AKTIVE SITZUNG", EN: "ACTIVE SESSION") |
| Separator below dropdown | LAYOUT-03 | Visual check | Verify horizontal line visible between dropdown and Context Window |
| Context Window between Active Session and 5-Hour Window | LAYOUT-04 | Visual check | Verify section order in running app |
| Footer scrolls with content | LAYOUT-05 | Visual/interaction check | Scroll down to see footer; verify it is not fixed |
| Separator between Models and Input in Statistics | LAYOUT-06 | Visual check | Open Statistics section, verify separator line between Models and Input rows |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 15s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
