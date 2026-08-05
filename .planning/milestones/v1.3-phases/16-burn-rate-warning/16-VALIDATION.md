---
phase: 16
slug: burn-rate-warning
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-04-13
---

# Phase 16 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 / .NET 9 |
| **Config file** | `CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |
| **Quick run command** | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~BurnRate"` |
| **Full suite command** | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |
| **Estimated runtime** | ~15 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~BurnRate"`
- **After every plan wave:** Run `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 15 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 16-01-01 | 01 | 1 | BURN-01, BURN-02, BURN-03 | unit | `dotnet test --filter "BurnRateCalculator\|BurnRateFormatter"` | :x: W0 | :white_large_square: pending |
| 16-01-02 | 01 | 1 | BURN-06, BURN-07 | build | `dotnet build` | :white_check_mark: | :white_large_square: pending |
| 16-02-01 | 02 | 2 | BURN-04, BURN-05, BURN-07 | build | `dotnet build` | :white_check_mark: | :white_large_square: pending |
| 16-02-02 | 02 | 2 | BURN-04 | build | `dotnet build` | :white_check_mark: | :white_large_square: pending |
| 16-02-03 | 02 | 2 | BURN-04, BURN-05 | manual | screenshot + toast verification | N/A | :white_large_square: pending |

*Status: :white_large_square: pending · :white_check_mark: green · :x: red · :warning: flaky*

---

## Wave 0 Requirements

- [ ] `CCInfoWindows.Tests/Helpers/BurnRateCalculatorTests.cs` — stubs for BURN-01, BURN-02, BURN-03
- [ ] `CCInfoWindows.Tests/Helpers/BurnRateFormatterTests.cs` — stubs for time formatting (DRY helper)
- [ ] Existing test infrastructure covers build verification

*Existing test project and xUnit framework already configured.*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Red banner appears with flame icon | BURN-04 | Visual UI verification | Run app, consume tokens rapidly, verify banner appears |
| Banner auto-dismiss on rate drop | BURN-04 | Runtime state change | Wait for usage rate to drop, verify banner disappears |
| Toast notification fires once | BURN-05 | OS notification system | Trigger warning, verify toast in Action Center |
| Toast does not re-fire | BURN-07 | Cycle behavior | Verify toast only fires once per warning cycle |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 15s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
