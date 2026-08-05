---
phase: 17
slug: chart-horizontal-gradient
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-04-13
---

# Phase 17 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 / .NET 9 |
| **Config file** | `CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |
| **Quick run command** | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~ChartRenderer"` |
| **Full suite command** | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |
| **Estimated runtime** | ~15 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~ChartRenderer"`
- **After every plan wave:** Run `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 15 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 17-01-01 | 01 | 1 | CHRT-01, CHRT-03 | unit | `dotnet test --filter "ChartRenderer"` | :white_check_mark: | :white_large_square: pending |
| 17-01-02 | 01 | 1 | CHRT-01, CHRT-02, CHRT-05 | build | `dotnet build` | :white_check_mark: | :white_large_square: pending |
| 17-02-01 | 02 | 2 | CHRT-04 | build | `dotnet build` | :white_check_mark: | :white_large_square: pending |
| 17-02-02 | 02 | 2 | CHRT-01, CHRT-04, CHRT-05 | manual | screenshot verification | N/A | :white_large_square: pending |

*Status: :white_large_square: pending · :white_check_mark: green · :x: red · :warning: flaky*

---

## Wave 0 Requirements

- [ ] Existing `CCInfoWindows.Tests/Helpers/ChartRendererTests.cs` extended with gradient stop tests
- [ ] Existing test infrastructure covers build verification

*Existing test project and xUnit framework already configured.*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Gradient fill smooth green→red | CHRT-01 | Visual rendering | Run app with usage data, inspect chart area gradient |
| Line stroke 100% over fill 25% | CHRT-02 | Visual opacity check | Compare line and fill opacity visually |
| No gradient bleed into gaps | CHRT-03 | Visual gap check | Verify empty chart areas have no color |
| Export PNG matches live | CHRT-04 | File comparison | Export chart, compare with live screenshot |
| No desaturation artifacts | CHRT-05 | Visual theme check | Switch dark/light mode, inspect gradient colors |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 15s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
