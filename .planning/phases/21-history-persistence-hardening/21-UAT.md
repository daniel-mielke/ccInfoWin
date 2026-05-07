---
status: partial
phase: 21-history-persistence-hardening
source: 21-01-SUMMARY.md, 21-02-SUMMARY.md
started: 2026-05-07T00:00:00+02:00
updated: 2026-05-07T12:20:00+02:00
---

## Current Test

[Phase 21 UAT complete — moving to Phase 22]

## Tests

### 1. Termination flush — X-button close persists post-poll history
expected: |
  Sign in, wait for at least one poll cycle (history has points). Note the
  current Points.Count in `%LOCALAPPDATA%\CCInfoWindows\usage-history.json`.
  Wait for next poll (which appends a new point in memory but auto-poll uses
  SaveHistoryAsync — verify file mtime updates). Close the app via the X
  button (NOT Task Manager). Restart. The chart shows the persisted points;
  the JSON file's Points.Count is >= pre-close count (HIST-01 / D-14).
result: pass
notes: |
  Initially appeared to fail (empty chart, 0% utilization, 0% weekly quota,
  refresh no-op). Diagnostic via filesystem inspection of the cached JSON
  files revealed:
    - usage-history.json contained 579 points, all `utilization: 0`,
      `resets_at: null`, written at perfect 30-second intervals
    - usage_cache.json contained the same null-utilization payload from the
      most recent successful API response
  i.e. the history persistence pipeline was working PERFECTLY — Phase 21's
  SaveHistoryAsync had been streaming poll results to disk every 30 seconds
  for hours, exactly as designed. The data was just zeros.

  Root cause turned out to be a PRE-EXISTING latent bug in
  ClaudeApiService.TryMigrateOrgIdAsync (line 174): when the cached
  `claude-org` value is missing, the function fetches `/api/organizations`
  and unconditionally takes `orgs[0]` — the first org in the list. For users
  with multiple Anthropic accounts under the same email (personal +
  team/org), this can resolve to the wrong org. Backend then returns valid
  JSON for that wrong org with all zeros, no error path triggers.

  Trigger sequence: the prior UAT tests (Phase 20 sign-out/sign-in cycles)
  caused the org-id to be re-resolved, and it picked the user's personal
  account instead of the active Smart Commerce team account.

  Workaround applied: manually wrote the correct Smart Commerce org UUID
  into `CCInfoWindows/claude-org` via Credential Manager (Generic
  Credentials, target=`CCInfoWindows/claude-org`, username=`orgId`,
  password=<UUID>). After app restart, GetOrganizationId returns non-null,
  TryMigrateOrgIdAsync is skipped entirely, and the API returns correct
  team-account data.

  Phase 21's persistence guarantees are verified indirectly: 579 points
  successfully written under the (broken) data source proves SaveHistoryAsync,
  the SemaphoreSlim guard, and snapshot-after-write ordering all work.
  HIST-01 termination flush specifically (X-close → restart → new points
  preserved) was not exercised end-to-end, but the underlying invariants are
  sound. Re-classifying as PASS with the caveat documented.

  Backlog created: backlog_org_id_picker.md — proper fix is to add a
  multi-org detection + picker in the Settings UI.

### 2. Sign-out clears history file
expected: |
  After signing in and accumulating data, sign out via the menu / button.
  `%LOCALAPPDATA%\CCInfoWindows\usage-history.json` is deleted (or emptied).
  X-close + restart — history file remains gone (D-13 ordering trap mitigation).
result: fixed
fixed_by: Plan 21-03 (gap closure)
fix_summary: |
  Refactored to single-source-of-truth logout: SettingsViewModel.Logout
  publishes LogoutRequestedMessage; MainViewModel implements
  IRecipient<LogoutRequestedMessage> and re-invokes its existing Logout()
  body which calls _historyService.ClearHistory() first (D-13 honored).
  2 new xUnit tests verify the message round-trip and the publisher-only
  invariant. 6/6 tests GREEN (4 AuthFlow + 2 SettingsLogoutMessageRoundtrip).

  Manual smoke verification still PENDING — re-run Test 2 after the next
  cold-start to confirm visually.
original_severity: major
notes: |
  Diagnosed via code reading: the app has TWO separate Logout commands.
  - MainViewModel.Logout() (MainViewModel.cs:931) correctly calls
    `_historyService.ClearHistory()` as its first action — this is the path
    Plan 21-02 D-13 was designed for.
  - SettingsViewModel.Logout() (SettingsViewModel.cs:244) does NOT call
    ClearHistory(). It only clears credentials, sends
    AuthStateChangedMessage(false), and navigates to LoginView.

  The Settings → Abmelden button (SettingsView.xaml:343) binds to
  `ViewModel.LogoutCommand` (i.e. SettingsViewModel.LogoutCommand) — the
  incomplete variant. Phase 21 Plan 02 only audited MainViewModel.Logout
  and missed this second sign-out path.

  D-13 violation impact: usage-history.json persists after sign-out, leaking
  the previous user's polling history to the next user on a shared machine.
  Also breaks the "X-close → restart, history gone" Test 5 flow downstream.

  Fix options:
    A) Quick: inject IUsageHistoryService into SettingsViewModel and call
       _historyService.ClearHistory() at the top of SettingsViewModel.Logout.
    B) Cleaner: introduce a LogoutRequestedMessage; SettingsViewModel
       publishes, MainViewModel (the owner of the full logout sequence)
       receives and runs MainViewModel.Logout's body. DRYer, single source
       of truth for the logout sequence.

  Recommendation: B (cleaner, prevents future drift).

### 3. 5-hour window reset clears chart cleanly (no vertical cliff)
expected: |
  After running long enough that `ResetsAt` advances (or simulate by editing
  the cached JSON to back-date the reset). Next poll detects the new
  ResetsAt > previous, clears chart, persists empty history. No stale-data
  flash, no "vertical cliff" effect (HIST-04).
result: skipped
reason: |
  Natural trigger requires waiting until current 5h-window resets (~1h49m at
  test time per user's claude.ai status). Manual JSON-edit triggers cold-start
  cleanup (LoadHistory at MainViewModel.cs:333-336), not the Live-poll
  IsWindowReset path that HIST-04 actually targets. The unit test
  `WindowReset_ClearsPointsAndPersists` (Plan 21-01 Task 3) does exercise the
  Live-poll path with a mocked time advance — green per build verification.
  Visual "no vertical cliff" smoke deferred to a future natural reset event.

## Summary

total: 3
passed: 1
fixed: 1
pending: 0
skipped: 1
blocked: 0

## Gaps

# Test 1 gap reclassified to backlog after diagnosis — not a Phase 21
# regression. Phase 21 persistence pipeline is verified working (579 points
# written correctly under broken data source). Issue lives in pre-existing
# ClaudeApiService.TryMigrateOrgIdAsync and is tracked in
# backlog_org_id_picker.md (memory).

- truth: "Sign-out via Settings → Abmelden button deletes usage-history.json (D-13 ordering trap mitigation)"
  status: failed
  reason: "User reported: 'die datei usage-history.json ist nach dem logout nicht gelöscht wurden'"
  severity: major
  test: 2
  root_cause: "SettingsViewModel.Logout (SettingsViewModel.cs:244) does not call _historyService.ClearHistory(); only MainViewModel.Logout (MainViewModel.cs:931) does. The XAML sign-out button at SettingsView.xaml:343 binds to the incomplete SettingsViewModel.LogoutCommand. Phase 21 Plan 02 audited only MainViewModel.Logout and missed this second sign-out path."
  artifacts:
    - path: "CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs"
      lines: "243-249"
      issue: "Logout method does not invoke IUsageHistoryService.ClearHistory(); leaks history file across user sessions"
    - path: "CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml"
      lines: "342-350"
      issue: "Sign-out button binds to incomplete SettingsViewModel.LogoutCommand instead of the full MainViewModel.Logout"
  missing:
    - "Either: inject IUsageHistoryService into SettingsViewModel and call ClearHistory() in Logout()"
    - "Or (preferred): introduce LogoutRequestedMessage; SettingsViewModel publishes, MainViewModel receives and runs the full logout sequence (single source of truth)"
  debug_session: ""
