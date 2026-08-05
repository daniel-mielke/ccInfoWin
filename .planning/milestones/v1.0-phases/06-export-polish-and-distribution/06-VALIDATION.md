---
phase: 6
slug: export-polish-and-distribution
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-16
---

# Phase 6 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.3 |
| **Config file** | CCInfoWindows.Tests/CCInfoWindows.Tests.csproj |
| **Quick run command** | `dotnet test CCInfoWindows.Tests/ -c Release -r win-x64 --no-build --filter "Category!=RequiresGPU"` |
| **Full suite command** | `dotnet test CCInfoWindows.Tests/ -c Release -r win-x64` |
| **Estimated runtime** | ~15 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test CCInfoWindows.Tests/ -c Release -r win-x64 --no-build --filter "Category!=RequiresGPU"`
- **After every plan wave:** Run `dotnet test CCInfoWindows.Tests/ -c Release -r win-x64`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 15 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 06-01-01 | 01 | 1 | EXPT-01, EXPT-03 | unit | `dotnet test ... --filter ExportHelperTests` | ❌ W0 | ⬜ pending |
| 06-01-02 | 01 | 1 | EXPT-02 | unit | `dotnet test ... --filter ExportHelperTests` | ❌ W0 | ⬜ pending |
| 06-02-01 | 02 | 1 | UPDT-01 | unit | `dotnet test ... --filter UpdateServiceTests` | ❌ W0 | ⬜ pending |
| 06-02-02 | 02 | 1 | SETT-02 | unit | `dotnet test ... --filter RegistryHelperTests` | ❌ W0 | ⬜ pending |
| 06-02-03 | 02 | 1 | UIPF-05 | verify-only | existing SettingsService tests | manual check | ⬜ pending |
| 06-03-01 | 03 | 2 | SETT-04 | manual-only | n/a — requires WinUI runtime | n/a | ⬜ pending |
| 06-03-02 | 03 | 2 | UIPF-07 | manual-only | n/a — requires Narrator/NVDA | n/a | ⬜ pending |
| 06-03-03 | 03 | 2 | DIST-01, DIST-02, DIST-03 | manual-only | Inno Setup Compiler run | n/a | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `CCInfoWindows.Tests/Helpers/ExportHelperTests.cs` — stubs for EXPT-01/03 (marked `[Trait("Category", "RequiresGPU")]`)
- [ ] `CCInfoWindows.Tests/Services/UpdateServiceTests.cs` — stubs for UPDT-01 SemVer parsing
- [ ] `CCInfoWindows.Tests/Helpers/RegistryHelperTests.cs` — stubs for SETT-02 autostart read/write

*Note: Win2D CanvasRenderTarget requires GPU — export tests must be marked and skipped in headless CI.*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Language switch changes all UI strings | SETT-04 | Requires WinUI 3 runtime + WinUI3Localizer | 1. Open Settings 2. Switch language 3. Verify all strings update |
| Accessibility labels on all buttons | UIPF-07 | Requires Narrator/NVDA screen reader | 1. Enable Narrator 2. Tab through UI 3. Verify all elements announced |
| Inno Setup installer builds and installs | DIST-01 | Requires Inno Setup Compiler + manual install test | 1. Run ISCC on .iss 2. Run installer 3. Verify app launches from install path |
| Autostart toggle persists across reboot | SETT-02 | Requires system restart verification | 1. Enable autostart 2. Restart 3. Verify app starts |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 15s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
