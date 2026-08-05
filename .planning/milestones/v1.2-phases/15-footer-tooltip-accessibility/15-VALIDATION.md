---
phase: 15
slug: footer-tooltip-accessibility
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-04-12
---

# Phase 15 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | MSTest / .NET 9 |
| **Config file** | `CCInfoWindows/CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |
| **Quick run command** | `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` |
| **Full suite command** | `dotnet test CCInfoWindows/CCInfoWindows.Tests` |
| **Estimated runtime** | ~15 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj`
- **After every plan wave:** Run `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj`
- **Before `/gsd:verify-work`:** Build must pass
- **Max feedback latency:** 10 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 15-01-01 | 01 | 1 | ACC-01, ACC-02, ACC-03 | build + manual | `dotnet build` | ✅ | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

*Existing infrastructure covers all phase requirements — no new test framework needed.*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Tooltip on Refresh button | ACC-01 | Hover requires running app | Launch app → hover Refresh → verify tooltip |
| Tooltip on Settings button | ACC-01 | Hover requires running app | Launch app → hover Settings → verify tooltip |
| Tooltip on Quit button | ACC-01 | Hover requires running app | Launch app → hover Quit → verify tooltip |
| Screen reader announces buttons | ACC-02 | Requires Accessibility Insights or Narrator | Run Accessibility Insights → verify Name on each button |
| Tooltips in correct language | ACC-03 | Language switch requires running app | Switch language → verify tooltip text changes |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 10s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
