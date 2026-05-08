# Phase 28: v1.4 Cleanup & Final UAT - Context

**Gathered:** 2026-05-08
**Status:** Ready for planning
**Mode:** Smart-discuss auto-resolved (autonomous run; defaults from REQUIREMENTS, ROADMAP, todos)

<domain>
## Phase Boundary

Phase 28 is the **final v1.5 phase**: cleanup of remaining v1.4 code-review remediation + G-3 convention documentation + opportunistic Nits commit + full milestone-level UAT pass.

**Strict scope:** No new features. Pure remediation. The 4 CLEANUP requirements are explicit; no scope creep into Phase 25/26/27 territory. Final UAT validates v1.5 ships clean by re-verifying all 24-27 success criteria in a single end-to-end pass.

</domain>

<decisions>
## Implementation Decisions

### CLEANUP-01: Delete Messages/LogoutRequestedMessage.cs
- **Verified scope:** Only 2 references exist in the codebase: (a) the file itself (`Messages/LogoutRequestedMessage.cs`), (b) a comment in `MainViewModel.cs:54` documenting the v1.4 revert. Both removed in this phase.
- **Action:** delete the file; update or remove the MainViewModel.cs:54 comment (the historical context is preserved in git history + PROJECT.md D-13).

### CLEANUP-02: Replace `_contextModelBadgeColor = null!` with real default
- **Current state:** `MainViewModel._contextModelBadgeColor` was set to `null!` at some point. The visible behavior when no model is yet detected should be a gray badge.
- **Fix:** initialize via a real default — recommended `ParseHexBrush("#9CA3AF")` (Tailwind gray-400 — matches typical inactive-badge Tone) OR reuse an existing brush helper from the ViewModel.
- **Test:** add a unit test that constructs MainViewModel and asserts `_contextModelBadgeColor` is not null and matches the gray-fallback color BEFORE any usage data is loaded.
- **Convention:** this is the FIRST G-3 fix; CLAUDE.md will document the convention afterward (CLEANUP-04).

### CLEANUP-03: Bundle 3 Nits into single commit
- **N-1:** Delete redundant `if (ViewModel == null) return;` guard in `Views/SettingsView.xaml.cs OnSegmentedSelectionChanged`. ViewModel is constructor-injected and never null.
- **N-2:** Tighten bare `catch` on `Localizer.Get().GetLocalizedString()` in `MainViewModel.ComputeTooltipText`. Either remove entirely or narrow. Recommendation: remove entirely — Phase 23 + Phase 24+ ResourceCoverageTests guarantee the key exists.
- **N-3:** Remove duplicate `private const int AboutTabIndex` in `Views/SettingsView.xaml.cs:13`. Reference `SettingsViewModel.AboutTabIndex` directly. **NOTE:** Phase 26 shifted AboutTabIndex from 3 to 4 (added SessionsTabIndex=3) — Plan Phase verifies the cleanup honors the new index.
- **Single commit:** all 3 nits in one `chore: bundle v1.4 nits cleanup` commit.

### CLEANUP-04: Document G-3 convention in CLAUDE.md
- **Convention text:** "Prefer `= string.Empty;`, `= "--";`, or `= ParseHexBrush(...)` initializers over `null!` for `[ObservableProperty]` fields. Reason: `null!` defers a NullReferenceException to first read, which can fire from any binding evaluation site without clear stack trace context. Real defaults are testable, predictable, and preserve the visible behavior even before async initialization completes. Precedent: `MainViewModel._contextModelBadgeColor` (M-3 fix in Phase 28 CLEANUP-02)."
- **Placement:** CLAUDE.md MVVM Conventions section, after the existing G-1 and G-2 paragraphs.

### Final UAT (success criterion #5)
- **Scope:** re-verify every Phase 24-27 success criterion in a single end-to-end pass. Run all convention tests + run the app + manually validate all 12 deferred Visual UAT items from Phases 25-27:
  - Phase 25 (3): toast first-launch, dismiss persistence, ComboBox visual position
  - Phase 26 (4): pencil dialog UX, 5-tab 360px fit, cross-tab live update on rename, orphan greyed-out display
  - Phase 27 (~5): NextWindow label visibility, Pricing InfoBar surfacing/clearing, OrgPicker dialog flow, soft-prompt trigger after 5 zero-utilization polls, "Don't show again" suppression
- **Per user directive ("Nie pausieren, am Ende alles validieren"):** the Final UAT step IS the validation pause — at this point, the user explicitly checks everything before milestone close.
- **No new test failures expected:** the 2 pre-existing ClaudeApiServiceTests failures stay; no Phase 28 changes should affect them.

### Carrying Forward
- **L-01:** No new IDispatcherQueue usage. Phase 28 is pure remediation.
- **L-02:** No new IRecipient handlers. Convention test stays green.
- **L-03:** No new JSON store. G-2 not consumed.
- **L-04:** AboutTabIndex is now 4 (from Phase 26 SessionsTab introduction). N-3 reference must use the up-to-date constant.

### Out of Scope
- **O-01:** Investigating the 2 pre-existing ClaudeApiServiceTests failures (`FetchUsageAsync_OnTransientNullResponse_RetriesAndSucceeds` + `FetchUsageAsync_OnPersistentNullResponse_ThrowsAfterRetries`) — pre-date Phase 24, deferred to v1.6+ unless they newly block.
- **O-02:** Performance optimization passes.
- **O-03:** New convention additions beyond G-3.

### Claude's Discretion
- **CD-01: ContextModelBadgeColor default value.** Recommended `ParseHexBrush("#9CA3AF")` (gray-400). Plan Phase verifies this matches the intended pre-data fallback color from the styleguide.
- **CD-02: Comment on MainViewModel.cs:54.** Two options: (a) delete entirely (D-13 is in PROJECT.md), (b) shorten to a brief comment citing PROJECT.md D-13. Recommendation: (a) delete — historical context is durably preserved.
- **CD-03: N-2 catch handling.** Remove vs narrow. Recommendation: remove (Phase 24's ResourceCoverageTests provides structural guarantee; Phase 27's L10N refactor extends coverage).
- **CD-04: Final UAT format.** Single end-to-end smoke pass vs structured per-feature checklist. Recommendation: structured per-feature checklist — easier to track which UAT items pass/fail.

</decisions>

<canonical_refs>
## Canonical References

### Phase 28 deliverable scope
- `.planning/REQUIREMENTS.md` §"Cluster C cleanup wave" — CLEANUP-01..04.
- `.planning/ROADMAP.md` §"Phase 28" success criteria — 5 criteria.
- `.planning/todos/pending/2026-05-07-m1-delete-orphan-logoutrequestedmessage.md` — CLEANUP-01 source.
- `.planning/todos/pending/2026-05-07-m3-revert-contextmodelbadgecolor-default-to-gray.md` — CLEANUP-02 source.
- `.planning/todos/pending/2026-05-07-nits-v14-code-review-cleanups.md` — CLEANUP-03 source (N-1, N-2, N-3 details).

### In-tree code anchors
- `CCInfoWindows/CCInfoWindows/Messages/LogoutRequestedMessage.cs` — DELETE.
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs:54` — comment removal.
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs:_contextModelBadgeColor` field — replace null! with ParseHexBrush(...).
- `CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml.cs:OnSegmentedSelectionChanged` — N-1 redundant null-guard.
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs:ComputeTooltipText` — N-2 bare catch.
- `CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml.cs:13` — N-3 duplicate AboutTabIndex (Phase 26 shifted to 4).
- `CLAUDE.md` MVVM Conventions section — CLEANUP-04 G-3 paragraph target.

### Test target
- New `CCInfoWindows.Tests/ViewModels/MainViewModelInitialStateTests.cs` (or extend existing): assert `_contextModelBadgeColor` is non-null at construction time and equals the documented gray fallback.
- All convention tests must continue to pass: ResourceCoverageTests, MessengerThreadingConventionTests, BannerStackPolicyTests.

### Visual UAT items deferred from Phases 25-27 (~12 items)
- See SUMMARY.md "Visual Smoke Deferred" sections in each phase folder.

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- **`ParseHexBrush(...)` helper** — already exists in MainViewModel (used for badge color computation elsewhere). Reuse for default initialization.
- **G-1 + G-2 paragraphs in CLAUDE.md** — template for G-3 convention paragraph.
- **`SettingsViewModel.AboutTabIndex` public const** — exists as the canonical reference value.

### Established Patterns
- **Single-commit cleanup waves** — Phase 24's NuGet bumps used the same approach (Plan 24-03 bundled bumps + docs in single wave).
- **Convention documentation in CLAUDE.md MVVM Conventions section** — G-1 (Phase 24) + G-2 (implicit from Phase 26 G-2 pattern usage) + G-3 (this phase).

### Integration Points
- **CLEANUP-04 placement:** CLAUDE.md MVVM Conventions section, after G-1 and G-2 paragraphs.
- **CLEANUP-02 test:** new test in MainViewModelInitialStateTests.cs uses standard FakeDispatcherQueue + FakeSessionNameStore from Phase 26.

</code_context>

<specifics>
## Specific Ideas

- **CLEANUP-02 default value sketch:**
  ```csharp
  [ObservableProperty]
  private SolidColorBrush _contextModelBadgeColor = ParseHexBrush("#9CA3AF");
  ```
- **CLEANUP-04 G-3 paragraph:**
  > **G-3 — `[ObservableProperty]` default value rule (PREFERRED, not enforced):** Prefer `= string.Empty;`, `= "--";`, or `= ParseHexBrush(...)` initializers over `null!` for `[ObservableProperty]` fields. `null!` defers a NullReferenceException to first read, which can fire from any binding evaluation site without clear stack trace context. Real defaults are testable, predictable, and preserve the visible behavior even before async initialization completes. Precedent: `MainViewModel._contextModelBadgeColor` (Phase 28 CLEANUP-02 fix).

- **Final UAT checklist sketch (success criterion #5):**
  ```
  ## v1.5 Final UAT Checklist
  
  ### Phase 24 — Dispatcher Foundation (no UAT — convention tests cover)
  - [x] MessengerThreadingConventionTests passes
  
  ### Phase 25 — Cold-Start Session Hydration & Visibility Window
  - [ ] Migration toast appears on first launch after upgrade
  - [ ] Toast dismiss persists (kill app between dismiss + relaunch verifies)
  - [ ] SessionVisibilityWindow ComboBox visible in Settings General tab
  - [ ] Changing window value re-filters dropdown immediately
  
  ### Phase 26 — Persistent Session Renaming
  - [ ] Pencil button next to ComboBox opens dialog
  - [ ] Save persists name, ComboBox updates without restart
  - [ ] Reset button visible only when custom name exists
  - [ ] Settings Sessions tab visible (5th segment)
  - [ ] 5-tab Segmented Control fits at 360px width
  - [ ] Cross-tab live update: rename in MainView dialog → Settings tab updates
  - [ ] Orphan custom names display greyed with subtitle
  
  ### Phase 27 — NEXTWIN + ORGID + PRICING + L10N
  - [ ] NextWindow label appears below 5h-countdown when ResetsAt is non-null
  - [ ] Label hidden (Visibility=Collapsed) when ResetsAt is null OR auth banner shows
  - [ ] LastFetchRelativeTime shows German/English text per CurrentUICulture
  - [ ] PricingError InfoBar appears when EnsurePricesLoadedAsync fails (simulate by network kill)
  - [ ] PricingError InfoBar disappears on subsequent success
  - [ ] PricingError InfoBar suppressed when auth banner shows (banner stack policy)
  - [ ] Re-detect button on Settings Account tab opens OrgPicker dialog
  - [ ] OrgPicker shows organization list with name+uuid
  - [ ] Switch triggers Logout → LoginView (verify cookie reset by inspecting WebView2 UDF)
  - [ ] OrgMismatch InfoBar appears after 5 consecutive zero-utilization polls
  - [ ] "Don't show again this session" suppresses for current session only
  ```

</specifics>

<deferred>
## Deferred Ideas

- **Pre-existing ClaudeApiServiceTests failures investigation** — v1.6+ unless they block ship.
- **Performance optimization** — out of scope.
- **Additional convention additions beyond G-3** — out of scope.

</deferred>

---

*Phase: 28-v1-4-Cleanup-Final-UAT*
*Context gathered: 2026-05-08 — final v1.5 phase*
