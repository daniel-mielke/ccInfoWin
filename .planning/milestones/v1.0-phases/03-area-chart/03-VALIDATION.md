---
phase: 03
slug: area-chart
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-11
---

# Phase 03 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xunit 2.9.3 + Moq 4.20.72 |
| **Config file** | none — discovery via xunit runner |
| **Quick run command** | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -c Debug --no-build` |
| **Full suite command** | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -c Debug` |
| **Estimated runtime** | ~5 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -c Debug --no-build`
- **After every plan wave:** Run `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -c Debug`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 10 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 03-01-01 | 01 | 1 | 5HUR-07 | unit | `dotnet test ... --filter "FullyQualifiedName~UsageHistoryServiceTests"` | ❌ W0 | ⬜ pending |
| 03-01-02 | 01 | 1 | 5HUR-08 | unit | `dotnet test ... --filter "FullyQualifiedName~UsageHistoryServiceTests"` | ❌ W0 | ⬜ pending |
| 03-02-01 | 02 | 2 | 5HUR-03 | unit | `dotnet test ... --filter "FullyQualifiedName~UsageChartRendererTests"` | ❌ W0 | ⬜ pending |
| 03-02-02 | 02 | 2 | 5HUR-04 | unit | `dotnet test ... --filter "FullyQualifiedName~ColorThresholdsTests"` | ✅ | ⬜ pending |
| 03-02-03 | 02 | 2 | 5HUR-05 | unit | `dotnet test ... --filter "FullyQualifiedName~UsageChartRendererTests"` | ❌ W0 | ⬜ pending |
| 03-02-04 | 02 | 2 | 5HUR-06 | unit | `dotnet test ... --filter "FullyQualifiedName~UsageChartRendererTests"` | ❌ W0 | ⬜ pending |
| 03-02-05 | 02 | 2 | 5HUR-09 | manual-only | N/A | N/A | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `CCInfoWindows.Tests/Services/UsageHistoryServiceTests.cs` — stubs for 5HUR-07, 5HUR-08
- [ ] `CCInfoWindows.Tests/Helpers/UsageChartRendererTests.cs` — stubs for 5HUR-03, 5HUR-05, 5HUR-06 (pure coordinate math)

*5HUR-04 already covered by existing `ColorThresholdsTests.cs`*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Dark mode color values used in dark theme | 5HUR-09 | ThemeResource color extraction is a runtime UI concern; no unit test surface | 1. Launch app in dark mode 2. Verify chart uses desaturated zone colors 3. Toggle to light mode, verify standard zone colors |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 10s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
