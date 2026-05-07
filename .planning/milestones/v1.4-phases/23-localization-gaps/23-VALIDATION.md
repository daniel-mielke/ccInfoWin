---
phase: 23
slug: localization-gaps
status: draft
nyquist_compliant: true
wave_0_complete: true
created: 2026-05-06
---

# Phase 23 — Validation Strategy

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 (existing) |
| **Test File** | `CCInfoWindows.Tests/Localization/ResourceCoverageTests.cs` (NEW) |
| **Quick run command** | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~ResourceCoverage"` |
| **Strategy** | XDocument-based structural validation (NOT runtime Localizer — cannot init in xUnit) |

## Per-Task Verification

| Task | Requirement | Test Type | Command |
|------|-------------|-----------|---------|
| T1: Add 4 keys × 2 locales | L10N-01 | Manual + automated | XDocument validates 6 keys per file |
| T2: Verify XAML migration | L10N-02 | Automated grep | `grep -rE '"(Loading|No data|Not signed in)"'` returns 0 (already trivially passing) |
| T3: Tests for resw structure | L10N-01 | Automated | xUnit test passes |

## Manual-Only Verifications

| Behavior | Requirement | Why Manual |
|----------|-------------|------------|
| Runtime language switch updates all 6 keys live | L10N-03 | Requires running app + WinUI3Localizer cache invalidation cycle |

## Validation Sign-Off

- [x] Wave 0: no scaffolding needed (existing test project)
- [ ] L10N-01: 6 keys present in both locales (verified per task acceptance grep)
- [ ] L10N-02: 0 hardcoded strings remain (already true — verified, not changed)
- [ ] L10N-03: Manual smoke recorded in VERIFICATION.md before phase verify
