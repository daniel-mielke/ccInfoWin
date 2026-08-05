---
phase: 13
slug: sonnet-context-window-setting
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-04-12
---

# Phase 13 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | MSTest / .NET 9 |
| **Config file** | `CCInfoWindows/CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |
| **Quick run command** | `dotnet test CCInfoWindows/CCInfoWindows.Tests --filter "ClassName~SettingsViewModel"` |
| **Full suite command** | `dotnet test CCInfoWindows/CCInfoWindows.Tests` |
| **Estimated runtime** | ~15 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj`
- **After every plan wave:** Run `dotnet test CCInfoWindows/CCInfoWindows.Tests`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 15 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 13-01-01 | 01 | 1 | SET-01 | build | `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` | ✅ | ⬜ pending |
| 13-01-02 | 01 | 1 | SET-02 | unit | `dotnet test --filter "ClassName~ModelContextLimits"` | ✅ | ⬜ pending |
| 13-01-03 | 01 | 1 | SET-03 | manual | N/A — UI live refresh requires running app | N/A | ⬜ pending |
| 13-01-04 | 01 | 1 | SET-04 | integration | `dotnet build` | ✅ | ⬜ pending |
| 13-01-05 | 01 | 1 | SET-05 | manual | N/A — localization requires visual verification | N/A | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] Verify `AppSettings.SonnetContextSize` property serialization round-trip
- [ ] Verify `ModelContextLimits.GetMaxContextTokens` with explicit sonnet param (already tested by Phase 12)

*Existing test infrastructure covers framework setup — no new framework install needed.*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| ComboBox visible in Settings view | SET-01 | WinUI 3 XAML rendering requires running app | Launch app → Settings → verify ComboBox with "200K" and "1M" |
| Live refresh on picker change | SET-03 | Requires running app with active session | Change Sonnet Context picker → verify context display updates without manual refresh |
| Localized labels | SET-05 | Visual verification of l:Uids.Uid rendering | Switch language → verify "Sonnet-Kontext" (DE) / "Sonnet Context" (EN) |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 15s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
