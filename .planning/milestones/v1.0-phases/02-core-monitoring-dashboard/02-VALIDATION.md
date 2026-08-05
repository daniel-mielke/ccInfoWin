---
phase: 02
slug: core-monitoring-dashboard
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-10
---

# Phase 02 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit + Moq (new test project -- Wave 0) |
| **Config file** | none -- Wave 0 installs |
| **Quick run command** | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "Category!=Integration"` |
| **Full suite command** | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |
| **Estimated runtime** | ~5 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "Category!=Integration"`
- **After every plan wave:** Run `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 10 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 02-01-01 | 01 | 0 | INFRA | unit | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` | ❌ W0 | ⬜ pending |
| 02-01-02 | 01 | 1 | 5HUR-01 | unit | `dotnet test --filter "FullyQualifiedName~UsageDataTests"` | ❌ W0 | ⬜ pending |
| 02-01-03 | 01 | 1 | 5HUR-02 | unit | `dotnet test --filter "FullyQualifiedName~CountdownFormatterTests"` | ❌ W0 | ⬜ pending |
| 02-01-04 | 01 | 1 | WEEK-01 | unit | `dotnet test --filter "FullyQualifiedName~UsageDataTests"` | ❌ W0 | ⬜ pending |
| 02-01-05 | 01 | 1 | WEEK-02 | unit | `dotnet test --filter "FullyQualifiedName~UsageDataTests"` | ❌ W0 | ⬜ pending |
| 02-01-06 | 01 | 1 | WEEK-03 | unit | `dotnet test --filter "FullyQualifiedName~DateFormatterTests"` | ❌ W0 | ⬜ pending |
| 02-02-01 | 02 | 1 | DATA-01 | unit | `dotnet test --filter "FullyQualifiedName~ClaudeApiServiceTests"` | ❌ W0 | ⬜ pending |
| 02-02-02 | 02 | 1 | DATA-02 | unit | `dotnet test --filter "FullyQualifiedName~ClaudeApiServiceTests"` | ❌ W0 | ⬜ pending |
| 02-03-01 | 03 | 2 | UIPF-04 | unit | `dotnet test --filter "FullyQualifiedName~ColorThresholdsTests"` | ❌ W0 | ⬜ pending |
| 02-03-02 | 03 | 2 | UIPF-02 | manual-only | Manual: visual inspection in both themes | N/A | ⬜ pending |
| 02-04-01 | 04 | 2 | SETT-01 | unit | `dotnet test --filter "FullyQualifiedName~SettingsTests"` | ❌ W0 | ⬜ pending |
| 02-04-02 | 04 | 2 | SETT-05 | manual-only | Manual: toggle switch and verify visual change | N/A | ⬜ pending |
| 02-04-03 | 04 | 2 | SETT-06 | unit | `dotnet test --filter "FullyQualifiedName~SettingsTests"` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` -- new test project (xUnit + Moq)
- [ ] `CCInfoWindows.Tests/Models/UsageDataTests.cs` -- covers 5HUR-01, WEEK-01, WEEK-02
- [ ] `CCInfoWindows.Tests/Helpers/ColorThresholdsTests.cs` -- covers UIPF-04
- [ ] `CCInfoWindows.Tests/Helpers/CountdownFormatterTests.cs` -- covers 5HUR-02, WEEK-03
- [ ] `CCInfoWindows.Tests/Services/ClaudeApiServiceTests.cs` -- covers DATA-01, DATA-02
- [ ] `CCInfoWindows.Tests/Models/AppSettingsTests.cs` -- covers SETT-01, SETT-06

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Opaque background in current theme | UIPF-02 | Visual rendering validation | Launch app in dark mode, verify progress bar backgrounds are opaque (#1E293B). Switch to light mode, verify backgrounds are opaque (#E2E8F0). |
| Theme toggle switch | SETT-05 | Visual rendering validation | Open settings, toggle theme switch, verify entire UI updates immediately without restart. |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 10s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
