# Phase 8: Documentation Hygiene & Verification - Research

**Researched:** 2026-03-17
**Domain:** Documentation audit, REQUIREMENTS.md checkbox correction, VERIFICATION.md authoring
**Confidence:** HIGH

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
None — pure infrastructure phase with no locked technical choices.

### Claude's Discretion
All implementation choices are at Claude's discretion — pure infrastructure phase.

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope.
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| CTXW-05 | When no active session exists, show 0% bar with "No active session" message | Implemented in Phase 4 Plan 3 (04-03). `HasActiveSession` ViewModel property + `InvertedBoolToVisibilityConverter` + `NoActiveSession` TextBlock in MainView.xaml. Checkbox update is documentation only. |
| SETT-05 | Manual dark/light mode toggle with immediate application | Implemented in Phase 2 Plan 4 (02-04). `SettingsView` ToggleSwitch + `ThemeChangedMessage` + `FrameworkElement.RequestedTheme` in MainWindow. 02-04-SUMMARY confirms "User can toggle dark/light mode via ToggleSwitch with immediate visual effect: VERIFIED". Checkbox update is documentation only. |
| EXPT-01 | 5-hour chart exportable as dark PNG via system save dialog | Implemented in Phase 6 Plan 1 (06-01). `ExportHelper.ExportChartAsPngAsync` with `FileSavePicker`. Confirmed by 06-VERIFICATION.md. Checkbox update is documentation only. |
| EXPT-02 | Thumbnail preview shown during export | Implemented in Phase 6 Plan 1 (06-01). Native `FileSavePicker` behavior (no custom code). Confirmed by 06-VERIFICATION.md (HUMAN required for runtime check). Checkbox update is documentation only. |
| EXPT-03 | Option to copy chart directly to clipboard | Implemented in Phase 6 Plan 1 (06-01). `ExportHelper.CopyChartToClipboardAsync` with `Clipboard.SetContent`. Confirmed by 06-VERIFICATION.md. Checkbox update is documentation only. |
| SETT-02 | Autostart option to launch app at Windows login | Implemented in Phase 6 Plan 1 (06-01). `RegistryHelper.SetAutostart`/`GetAutostart` + `SettingsViewModel.OnIsAutostartChanged`. Confirmed by 06-VERIFICATION.md. Checkbox update is documentation only. |
| UPDT-01 | Hourly check for new version via GitHub Releases API | Implemented in Phase 6 Plan 1 (06-01). `UpdateService` with `PeriodicTimer` at 3,600,000ms. Confirmed by 06-VERIFICATION.md. Checkbox update is documentation only. |
| UIPF-05 | Window position saved on close and restored on startup | Implemented in Phase 6 Plan 1 (06-01). `MainWindow.xaml.cs` `RestoreWindowState()` + `OnClosing` handler. Confirmed by 06-VERIFICATION.md. Checkbox update is documentation only. |
</phase_requirements>

---

## Summary

Phase 8 is a pure documentation correction phase. No code changes are involved. The work consists of two distinct operations: (1) updating 8 stale checkbox entries in `.planning/REQUIREMENTS.md` from `[ ]` to `[x]`, and (2) creating two missing VERIFICATION.md files for Phase 02 and Phase 04 that document what was actually implemented.

The stale checkboxes were explicitly noted in `06-VERIFICATION.md` as a documentation inconsistency: "REQUIREMENTS.md still marks EXPT-01/02/03, SETT-02, UPDT-01, UIPF-05 as unchecked (`[ ]`). This is a documentation inconsistency — the code implementations exist and are verified above." CTXW-05 was implemented in Phase 4 Plan 3 (`04-03-SUMMARY.md` lists `CTXW-05` in `requirements-completed`) but was never checked off. SETT-05 was implemented in Phase 2 Plan 4 (02-04-SUMMARY.md confirms it was VERIFIED).

Phases 02 and 04 both have a `VALIDATION.md` (pre-execution validation strategy) but no `VERIFICATION.md` (post-execution verification report). All other completed phases (01, 05, 06, 07) have a VERIFICATION.md. The Phase 06 VERIFICATION.md is the canonical template for this project.

**Primary recommendation:** Treat each stale checkbox as a look-up-and-confirm operation against the existing SUMMARY.md files, then write VERIFICATION.md files following the Phase 06 template exactly.

---

## Standard Stack

### Core
| Item | Version | Purpose | Why Standard |
|------|---------|---------|--------------|
| REQUIREMENTS.md checkbox format | — | `- [x] **REQ-ID**: Description` | Established in `.planning/REQUIREMENTS.md` |
| VERIFICATION.md frontmatter | — | YAML block with `phase`, `verified`, `status`, `score`, `human_verification` list | Established by 01/06 VERIFICATION.md files |
| VERIFICATION.md body sections | — | Goal Achievement, Required Artifacts, Key Link Verification, Requirements Coverage, Gaps Summary | Established by Phase 06 VERIFICATION.md |

### No Installation Required
This phase uses no libraries, no packages, and no build commands. All work is file editing only.

---

## Architecture Patterns

### Recommended Deliverable Structure
```
.planning/
├── REQUIREMENTS.md              -- 8 checkboxes updated: [ ] -> [x]
└── phases/
    ├── 02-core-monitoring-dashboard/
    │   └── 02-VERIFICATION.md   -- NEW: post-execution verification report
    └── 04-local-data-pipeline/
        └── 04-VERIFICATION.md   -- NEW: post-execution verification report
```

### Pattern 1: Checkbox Update (REQUIREMENTS.md)
**What:** Change `- [ ] **REQ-ID**: Description` to `- [x] **REQ-ID**: Description`
**When to use:** For each of the 8 stale requirements
**Line references in REQUIREMENTS.md:**
- Line 41: `CTXW-05` — `[ ]` → `[x]`
- Line 69: `EXPT-01` — `[ ]` → `[x]`
- Line 70: `EXPT-02` — `[ ]` → `[x]`
- Line 71: `EXPT-03` — `[ ]` → `[x]`
- Line 77: `SETT-02` — `[ ]` → `[x]`
- Line 79: `SETT-05` — `[ ]` → `[x]`
- Line 85: `UPDT-01` — `[ ]` → `[x]`
- Line 95: `UIPF-05` — `[ ]` → `[x]`

The Traceability table at the bottom must also have its "Pending" statuses changed to "Complete" for the same requirement IDs (lines 179, 195-197, 199, 202, 205, 212 in REQUIREMENTS.md).

### Pattern 2: VERIFICATION.md Frontmatter (from Phase 06 template)
```yaml
---
phase: 02-core-monitoring-dashboard
verified: 2026-03-17T00:00:00Z
status: passed
score: N/N must-haves verified
human_verification:
  - test: "..."
    expected: "..."
    why_human: "..."
---
```

### Pattern 3: VERIFICATION.md Body Structure
Based on Phase 06 template (06-VERIFICATION.md, 166 lines):

1. **Header block** — Phase goal, verified date, status, re-verification flag
2. **Goal Achievement** — Observable Truths table (from phase ROADMAP success criteria)
3. **Required Artifacts** — table of key files with expected/status/details
4. **Key Link Verification** — table of wiring between components
5. **Requirements Coverage** — table mapping each REQ-ID to source plan with evidence
6. **Anti-Patterns Found** — table (if none found, state "No blocker anti-patterns found")
7. **Human Verification Required** — section per item needing manual UI check
8. **Gaps Summary** — synthesis paragraph

### Anti-Patterns to Avoid
- **Creating VERIFICATION.md from scratch without reading SUMMARY.md files:** Each SUMMARY.md provides the exact evidence text (file names, line counts, commit hashes, self-check status) needed for verification reports.
- **Leaving Traceability table stale:** The REQUIREMENTS.md Traceability section at the bottom also has "Pending" statuses that need updating alongside the checkboxes.
- **Using wrong status for EXPT-02:** The 06-VERIFICATION.md marks EXPT-02 as `? HUMAN` (native FileSavePicker thumbnail behavior). The checkbox should be checked because the implementation is complete, but the VERIFICATION.md should note the human-only test for runtime confirmation.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Evidence for VERIFICATION.md | Scanning codebase manually | Read the SUMMARY.md files for each phase | SUMMARY.md files contain self-check results, commit hashes, and requirements-completed lists |
| Requirements traceability | Inferring from code | REQUIREMENTS.md Traceability section (lines 154-229) | Already maps every REQ-ID to phase and status |

**Key insight:** All evidence for Phase 02 and Phase 04 VERIFICATION.md files already exists in the SUMMARY.md files. No code scanning is required — just synthesize the SUMMARY.md self-check results into the VERIFICATION.md format.

---

## Common Pitfalls

### Pitfall 1: Forgetting the Traceability Table
**What goes wrong:** Checkboxes at the top of REQUIREMENTS.md are updated but the Traceability table at line ~154 still shows "Pending" for the same requirements.
**Why it happens:** The file has two places where requirement status appears.
**How to avoid:** After updating checkboxes, also update the Traceability table rows for CTXW-05, EXPT-01/02/03, SETT-02, SETT-05, UPDT-01, UIPF-05 from "Pending" to "Complete".
**Warning signs:** "Pending" in Traceability table next to requirements that have `[x]` checkboxes.

### Pitfall 2: Wrong Phase Attribution for SETT-05
**What goes wrong:** SETT-05 (dark/light mode toggle) might be attributed to Phase 6 because that phase added more settings, but it was completed in Phase 2 Plan 4.
**Why it happens:** Both Phase 2 and Phase 6 touched SettingsView.
**How to avoid:** REQUIREMENTS.md Traceability table (line 202) already has SETT-05 mapped to Phase 8 as "Pending" — but 02-04-SUMMARY.md confirms it was VERIFIED in Phase 2. The Traceability table's Phase assignment needs correction to Phase 2.

### Pitfall 3: CTXW-05 Phase Attribution
**What goes wrong:** CTXW-05 is listed in REQUIREMENTS.md Traceability as "Phase 8 | Pending" but 04-03-SUMMARY.md `requirements-completed` explicitly includes CTXW-05.
**Why it happens:** The REQUIREMENTS.md Traceability was set up to assign CTXW-05 to Phase 8 as a future task, but Phase 4 actually implemented it.
**How to avoid:** Update Traceability to Phase 4 | Complete (not Phase 8 | Complete).

### Pitfall 4: SETT-05 Phase Attribution in Traceability
**What goes wrong:** REQUIREMENTS.md Traceability (line 202) shows `SETT-05 | Phase 8 | Pending`.
**Why it happens:** Same as CTXW-05 — was pre-assigned to Phase 8 but implemented earlier.
**How to avoid:** Update Traceability to Phase 2 | Complete.

---

## Code Examples

### Phase 02 Requirements Completed (from SUMMARY.md files)
From `02-01-SUMMARY.md`: `requirements-completed: [UIPF-02, UIPF-04, 5HUR-01, 5HUR-02, WEEK-01, WEEK-02, WEEK-03, SETT-06]`
From `02-02-SUMMARY.md`: `requirements-completed: [DATA-01, DATA-02]`
From `02-03-SUMMARY.md`: `requirements-completed: [5HUR-01, 5HUR-02, WEEK-01, WEEK-02, WEEK-03, SETT-01, UIPF-02, UIPF-04]`
From `02-04-SUMMARY.md` (key files): `SettingsViewModel` + `SettingsView` + `ThemeChangedMessage` → SETT-01, SETT-05, SETT-06

### Phase 04 Requirements Completed (from SUMMARY.md files)
From `04-01-SUMMARY.md`: Data contracts, models, helpers (TOKS-01, SESS-01/05 partial)
From `04-02-SUMMARY.md`: IJsonlService implementation (DATA-03, DATA-04)
From `04-03-SUMMARY.md`: `requirements-completed: [SESS-01, SESS-02, SESS-03, SESS-04, SESS-05, CTXW-01, CTXW-02, CTXW-03, CTXW-04, CTXW-05, TOKS-01, SETT-03]`

### CTXW-05 Code Evidence
File: `CCInfoWindows/CCInfoWindows/Views/MainView.xaml`, line 344
```xml
<TextBlock l:Uids.Uid="NoActiveSession"
           Visibility="{x:Bind ViewModel.HasActiveSession, Mode=OneWay,
                        Converter={StaticResource InvertedBoolToVisibilityConverter}}" />
```
`HasActiveSession` property in `MainViewModel.cs` + `InvertedBoolToVisibilityConverter` converter implement the 0%/empty-state behavior.

---

## State of the Art

| Old Status | Current Status | Change | Impact |
|------------|---------------|--------|--------|
| CTXW-05: `[ ]` Pending (Phase 8) | `[x]` Complete (Phase 4) | Phase 4 Plan 3 implemented it | Traceability correction + checkbox |
| SETT-05: `[ ]` Pending (Phase 8) | `[x]` Complete (Phase 2) | Phase 2 Plan 4 implemented it | Traceability correction + checkbox |
| EXPT-01/02/03: `[ ]` Pending (Phase 8) | `[x]` Complete (Phase 6) | Phase 6 Plan 1 implemented them | Traceability correction + checkbox |
| SETT-02: `[ ]` Pending (Phase 8) | `[x]` Complete (Phase 6) | Phase 6 Plan 1 implemented it | Traceability correction + checkbox |
| UPDT-01: `[ ]` Pending (Phase 8) | `[x]` Complete (Phase 6) | Phase 6 Plan 1 implemented it | Traceability correction + checkbox |
| UIPF-05: `[ ]` Pending (Phase 8) | `[x]` Complete (Phase 6) | Phase 6 Plan 1 implemented it | Traceability correction + checkbox |
| Phase 02: No VERIFICATION.md | Phase 02 VERIFICATION.md exists | Gap closed | Audit record complete |
| Phase 04: No VERIFICATION.md | Phase 04 VERIFICATION.md exists | Gap closed | Audit record complete |

---

## Open Questions

1. **SETT-05 and CTXW-05 Phase attribution in Traceability**
   - What we know: REQUIREMENTS.md Traceability assigned both to "Phase 8 | Pending" but SUMMARY.md evidence proves Phase 2 and Phase 4 respectively implemented them.
   - What's unclear: Whether to update the Phase column in the Traceability table (correcting the historical mapping) or just update the Status.
   - Recommendation: Update both Phase and Status columns to reflect actual implementation history (Phase 2 / Phase 4 | Complete). This makes the Traceability table accurate as an audit record.

---

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 (existing test project) |
| Config file | `CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |
| Quick run command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "Category!=Integration" -p:Platform=x64` |
| Full suite command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -p:Platform=x64` |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| CTXW-05 | No active session shows 0% bar + message | manual-only | N/A — UI rendering + ViewModel state | N/A |
| SETT-05 | Dark/light mode toggle works immediately | manual-only | N/A — UI visual rendering | N/A |
| EXPT-01 | Chart exported as PNG via FileSavePicker | manual-only | N/A — native file dialog | N/A |
| EXPT-02 | Thumbnail preview shown in save dialog | manual-only | N/A — native FileSavePicker shell feature | N/A |
| EXPT-03 | Chart copied to clipboard | manual-only | N/A — clipboard content cannot be verified statically | N/A |
| SETT-02 | Autostart toggle writes registry entry | manual-only | N/A — requires running app + Task Manager check | N/A |
| UPDT-01 | Hourly GitHub Releases check | manual-only | N/A — requires mocked/real newer GitHub release | N/A |
| UIPF-05 | Window position persists across restart | manual-only | N/A — requires running app cycle | N/A |

**Note:** All Phase 8 requirements are documentation-only verifications of already-implemented features. No automated tests are needed or possible for this phase. The test suite passes green (verified in Phase 7) and no new tests are required.

### Sampling Rate
- **Per task commit:** `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -p:Platform=x64`
- **Per wave merge:** Same
- **Phase gate:** Full suite green (already passing from Phase 7)

### Wave 0 Gaps
None — existing test infrastructure covers all phase requirements. This phase makes no code changes.

---

## Sources

### Primary (HIGH confidence)
- `.planning/phases/06-export-polish-and-distribution/06-VERIFICATION.md` — canonical VERIFICATION.md template; confirms EXPT-01/02/03, SETT-02, UPDT-01, UIPF-05 are implemented and notes REQUIREMENTS.md staleness
- `.planning/phases/04-local-data-pipeline/04-03-SUMMARY.md` — `requirements-completed` explicitly lists CTXW-05
- `.planning/phases/02-core-monitoring-dashboard/02-04-SUMMARY.md` — self-check VERIFIED for SETT-05 (dark/light mode toggle)
- `.planning/REQUIREMENTS.md` — 8 stale checkboxes confirmed by reading file (lines 41, 69-71, 77, 79, 85, 95)
- `CCInfoWindows/CCInfoWindows/Views/MainView.xaml` — line 344: `NoActiveSession` TextBlock with `InvertedBoolToVisibilityConverter` confirms CTXW-05

### Secondary (MEDIUM confidence)
- `.planning/phases/01-foundation-and-authentication/01-VERIFICATION.md` — secondary template reference
- `.planning/phases/02-core-monitoring-dashboard/02-VALIDATION.md` — lists SETT-05 as manual-only at Phase 2 execution time
- `.planning/phases/04-local-data-pipeline/04-VALIDATION.md` — lists CTXW-05 as manual-only at Phase 4 execution time

---

## Metadata

**Confidence breakdown:**
- Implementation status of 8 requirements: HIGH — confirmed by SUMMARY.md evidence, 06-VERIFICATION.md note, and direct code file inspection
- VERIFICATION.md format: HIGH — Phase 06 template is complete and authoritative
- Traceability table corrections: HIGH — REQUIREMENTS.md line numbers verified, SUMMARY.md phase attribution confirmed

**Research date:** 2026-03-17
**Valid until:** 2026-04-17 (documentation only, not affected by library updates)
