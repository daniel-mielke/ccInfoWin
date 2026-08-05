---
phase: 1
slug: foundation-and-authentication
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-09
---

# Phase 1 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | MSTest 3.x |
| **Config file** | None — Wave 0 installs |
| **Quick run command** | `dotnet test --filter "Category=Unit"` |
| **Full suite command** | `dotnet test` |
| **Estimated runtime** | ~10 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet build`
- **After every plan wave:** Run `dotnet test`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 15 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 01-01-01 | 01 | 1 | SECU-06 | smoke | Verify .gitignore entries | ❌ W0 | ⬜ pending |
| 01-01-02 | 01 | 1 | UIPF-08 | smoke | `dotnet build` target verification | ❌ W0 | ⬜ pending |
| 01-02-01 | 02 | 1 | AUTH-02, SECU-02 | unit | `dotnet test --filter "CredentialService"` | ❌ W0 | ⬜ pending |
| 01-02-02 | 02 | 1 | AUTH-04 | unit | `dotnet test --filter "Logout"` | ❌ W0 | ⬜ pending |
| 01-02-03 | 02 | 1 | SECU-05 | unit | `dotnet test --filter "WebViewUdf"` | ❌ W0 | ⬜ pending |
| 01-03-01 | 03 | 2 | AUTH-01 | manual-only | N/A (WebView2 UI) | N/A | ⬜ pending |
| 01-03-02 | 03 | 2 | AUTH-03 | unit | `dotnet test --filter "TokenValidation"` | ❌ W0 | ⬜ pending |
| 01-03-03 | 03 | 2 | UIPF-01, UIPF-06 | unit | `dotnet test --filter "WindowSize"` | ❌ W0 | ⬜ pending |
| 01-03-04 | 03 | 2 | UIPF-03 | manual-only | Visual inspection | N/A | ⬜ pending |
| 01-03-05 | 03 | 2 | SECU-01 | smoke | grep scan for hardcoded patterns | ❌ W0 | ⬜ pending |
| 01-03-06 | 03 | 2 | SECU-03, SECU-04 | manual-only | Network inspection | N/A | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` — test project with MSTest
- [ ] `CCInfoWindows.Tests/Services/CredentialServiceTests.cs` — stubs for AUTH-02, AUTH-04, SECU-02
- [ ] `CCInfoWindows.Tests/Services/SettingsServiceTests.cs` — covers window state persistence
- [ ] `CCInfoWindows.Tests/Helpers/WindowHelperTests.cs` — covers UIPF-06 position validation
- [ ] MSTest framework install: `dotnet add CCInfoWindows.Tests package MSTest.TestFramework`

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| WebView2 shows claude.ai login | AUTH-01 | Requires browser interaction, WebView2 UI cannot be automated in unit test | Launch app, verify WebView2 loads claude.ai/login |
| Persistent standalone window | UIPF-01 | Visual window behavior verification | Launch app, verify title bar, minimize/restore, close behavior |
| Compact layout | UIPF-03 | Visual layout inspection | Launch app, verify layout matches spec |
| No telemetry/tracking | SECU-03 | Requires network traffic inspection | Use Fiddler/Wireshark, verify no outbound calls except claude.ai |
| Network only to claude.ai | SECU-04 | Requires network traffic inspection | Use Fiddler/Wireshark, verify no unexpected endpoints |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 15s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending

---

*Phase: 01-foundation-and-authentication*
*Validation strategy created: 2026-03-09*
