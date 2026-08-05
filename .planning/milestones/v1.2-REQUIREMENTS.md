# Requirements: ccInfoWin v1.2

**Defined:** 2026-04-12
**Core Value:** Developers can see their Claude usage limits at a glance in real-time, preventing unexpected throttling.

## v1.2 Requirements

Requirements for macOS v1.8.3 feature parity. Each maps to roadmap phases.

### Context Window Detection

- [x] **CTX-01**: User sees 1M context limit for Opus sessions (effective: ~967K after 33K buffer)
- [x] **CTX-02**: User sees 200K context limit for Haiku sessions (effective: ~167K)
- [x] **CTX-03**: User sees context limit based on configured Sonnet setting (200K or 1M)
- [x] **CTX-04**: User receives autocompact warning at 20K tokens remaining, regardless of model
- [x] **CTX-05**: User sees correct progress bar percentage reflecting model-based effective max
- [x] **CTX-06**: User sees correct context limits on subagent progress bars (model-based detection)

### Sonnet Context Setting

- [x] **SET-01**: User can configure Sonnet context size (200K or 1M) via ComboBox in Settings
- [x] **SET-02**: User sees default of 200K when no setting has been configured
- [x] **SET-03**: User sees context window display update immediately after changing the Sonnet setting
- [x] **SET-04**: User's Sonnet context setting persists across app restarts
- [x] **SET-05**: User sees localized labels for the Sonnet context picker (de-DE and en-US)

### Session Management

- [x] **SES-01**: User does not see sessions for deleted project directories in the session dropdown
- [x] **SES-02**: User sees session selection cleared when the selected project's directory is deleted
- [x] **SES-03**: User sees subagent context bars in stable alphabetical order by agent ID

### UI Accessibility

- [x] **ACC-01**: User sees localized tooltip when hovering each footer button (Refresh, Settings, Quit)
- [x] **ACC-02**: User's screen reader announces button purpose via AutomationProperties.Name
- [x] **ACC-03**: User sees tooltips in the correct language matching the current app language setting

## Future Requirements

### Deferred from Active (PROJECT.md)

- **V2-01**: System tray icon with quick status overview
- **V2-02**: Keyboard shortcuts for common actions
- **V2-03**: Configurable color thresholds for progress bars
- **V2-04**: Historical usage trends (daily/weekly graphs)
- **V2-05**: Migration to .NET 10 LTS when WinAppSDK confirms compatibility

## Out of Scope

| Feature | Reason |
|---------|--------|
| Per-model context size override (arbitrary number) | Claude models only come in 200K or 1M — ComboBox covers 100% of real-world cases |
| Auto-detect Sonnet context from API | API does not expose context window size; would require undocumented endpoint |
| Filter sessions by date/age | Different from orphan filtering; old-but-valid sessions should remain visible |
| Accessibility tree for Win2D chart canvas | Requires custom IAccessibleEx implementation — massively complex, niche |
| Updates Settings tab (Sparkle-style) | macOS-specific pattern; existing GitHub Releases poller + InfoBar banner sufficient |
| Sparkle auto-update framework | macOS-only (XPC/EdDSA); our GitHub Release checker already works |
| Notification threshold reset on sign-out | ccInfoWin has no desktop notification system |
| File-based credential storage | macOS Keychain workaround; Windows Credential Manager (DPAPI) works fine |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| CTX-01 | Phase 12 | Complete |
| CTX-02 | Phase 12 | Complete |
| CTX-03 | Phase 12 | Complete |
| CTX-04 | Phase 12 | Complete |
| CTX-05 | Phase 12 | Complete |
| CTX-06 | Phase 12 | Complete |
| SET-01 | Phase 13 | Complete |
| SET-02 | Phase 13 | Complete |
| SET-03 | Phase 13 | Complete |
| SET-04 | Phase 13 | Complete |
| SET-05 | Phase 13 | Complete |
| SES-01 | Phase 14 | Complete |
| SES-02 | Phase 14 | Complete |
| SES-03 | Phase 14 | Complete |
| ACC-01 | Phase 15 | Complete |
| ACC-02 | Phase 15 | Complete |
| ACC-03 | Phase 15 | Complete |

**Coverage:**
- v1.2 requirements: 16 total
- Mapped to phases: 16
- Unmapped: 0

---
*Requirements defined: 2026-04-12*
*Last updated: 2026-04-12 after initial definition*
