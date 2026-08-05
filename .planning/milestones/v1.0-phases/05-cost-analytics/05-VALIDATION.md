---
phase: 5
slug: cost-analytics
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-16
---

# Phase 5 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xunit 2.9.3 + Moq 4.20.72 |
| **Config file** | CCInfoWindows.Tests/CCInfoWindows.Tests.csproj |
| **Quick run command** | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "Category=Unit" -x64` |
| **Full suite command** | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -p:Platform=x64` |
| **Estimated runtime** | ~15 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "Category=Unit" -x64`
- **After every plan wave:** Run `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -p:Platform=x64`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 15 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 05-01-01 | 01 | 0 | COST-01, DATA-05 | unit | `dotnet test ... --filter "FullyQualifiedName~LiteLLMPricingServiceTests"` | ❌ W0 | ⬜ pending |
| 05-01-02 | 01 | 0 | COST-02, COST-03, COST-04 | unit | `dotnet test ... --filter "FullyQualifiedName~CostCalculatorTests"` | ❌ W0 | ⬜ pending |
| 05-01-03 | 01 | 0 | COST-05 | unit | `dotnet test ... --filter "FullyQualifiedName~BurnRateCalculatorTests"` | ❌ W0 | ⬜ pending |
| 05-01-04 | 01 | 1 | TOKS-03, TOKS-04 | unit | `dotnet test ... --filter "FullyQualifiedName~JsonlServiceTests"` | ✅ (new tests) | ⬜ pending |
| 05-02-01 | 02 | 2 | TOKS-02 | unit | `dotnet test ... --filter "FullyQualifiedName~StatisticsSummaryTests"` | ❌ W0 | ⬜ pending |
| 05-02-02 | 02 | 2 | COST-06 | manual | visual inspection in SettingsView | manual-only | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `CCInfoWindows.Tests/Services/LiteLLMPricingServiceTests.cs` — stubs for COST-01, DATA-05
- [ ] `CCInfoWindows.Tests/Services/CostCalculatorTests.cs` — stubs for COST-02, COST-03, COST-04
- [ ] `CCInfoWindows.Tests/Helpers/BurnRateCalculatorTests.cs` — stubs for COST-05
- [ ] `CCInfoWindows.Tests/Models/StatisticsSummaryTests.cs` — stubs for TOKS-02

*Existing infrastructure covers TOKS-03, TOKS-04 (new test methods in existing JsonlServiceTests.cs).*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Settings shows pricing data source and last fetch time | COST-06 | Visual layout verification | Open Settings → verify "Pricing source: Live API" or "Fallback" label + timestamp |
| Shimmer animation during tab switch | TOKS-02 | Animation rendering | Switch from Session to Monat tab → verify shimmer borders animate |
| Segmented control styling matches styleguide | TOKS-02 | Visual design | Compare tab bar against spec/v1.7.1/ccinfo-styleguide.md section 9.2 |

*All other phase behaviors have automated verification.*

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 15s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
