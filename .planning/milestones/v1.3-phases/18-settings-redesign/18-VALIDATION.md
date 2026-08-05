---
phase: 18
slug: settings-redesign
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-04-13
---

# Phase 18 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 / .NET 9 |
| **Config file** | `CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |
| **Quick run command** | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~Settings"` |
| **Full suite command** | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |
| **Estimated runtime** | ~15 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj`
- **After every plan wave:** Run `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 15 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 18-01-01 | 01 | 1 | SETT-01, SETT-06 | build | `dotnet build` | :white_check_mark: | :white_large_square: pending |
| 18-01-02 | 01 | 1 | SETT-02, SETT-08 | build | `dotnet build` | :white_check_mark: | :white_large_square: pending |
| 18-02-01 | 02 | 2 | SETT-03, SETT-04, SETT-05, SETT-07 | build | `dotnet build` | :white_check_mark: | :white_large_square: pending |
| 18-02-02 | 02 | 2 | SETT-01 through SETT-08 | manual | visual verification | N/A | :white_large_square: pending |

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Segmented Control with 4 tabs visible | SETT-01 | Visual UI | Open Settings, verify 4 tab control |
| Colored icon badges on tabs | SETT-06 | Visual UI | Inspect tab badges colors match spec |
| Tab switching smooth, no reload | SETT-05 | Interaction | Click through all tabs rapidly |
| 40px uniform rows in General | SETT-02 | Visual layout | Measure row heights visually |
| Short time notation | SETT-08 | Visual content | Verify 30s/1min/5min labels |
| Updates tab content | SETT-03 | Visual content | Check version, pricing source, fetch time |
| Account + About tab content | SETT-04 | Visual content | Check token, logout, credits, GitHub |
| DE/EN localization | SETT-07 | Runtime behavior | Switch language, verify all labels |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 15s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
