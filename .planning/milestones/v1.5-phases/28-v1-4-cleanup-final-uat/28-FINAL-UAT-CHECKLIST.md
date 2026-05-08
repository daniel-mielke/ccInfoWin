# v1.5 Final UAT Checklist
# Phase 28 — Pre-Milestone Close

**Milestone:** v1.5 macOS v1.12.0 Feature Parity + Hardening
**Phases covered:** 24 (foundation), 25 (dropdown), 26 (rename), 27 (NEXTWIN + ORGID + PRICING + L10N)
**Deferred items:** 17 total (3 from Ph25 + 4 from Ph26 + 10 from Ph27)
**All automated tests pass:** 342/344 (2 pre-existing ClaudeApiServiceTests failures excluded)

---

## How to use this checklist

1. Launch the app (`CCInfoWindows.exe`) with a valid claude.ai session.
2. Work through each section top to bottom.
3. Mark `[x]` for passed, `[!]` for failed (note what you saw).
4. Items marked `(cold-start)` require killing the app and relaunching.
5. Items marked `(live API)` require an active Claude Code session generating usage data.

---

## Phase 24 — Dispatcher Foundation (automated only)

All Phase 24 correctness is covered by automated convention tests.
No visual UAT items deferred from Phase 24.

- [x] `MessengerThreadingConventionTests` pass (2/2) — every `IRecipient<T>.Receive` either
      wraps in `TryEnqueue` or carries `[ThreadSafeReceive]` with a non-empty reason

---

## Phase 25 — Cold-Start Session Hydration & Visibility Window

**Context:** After an upgrade, the session ComboBox should list all recently-active sessions
within the configured visibility window. A one-time migration InfoBar informs users of the
new filter.

### 25-1  Migration toast — first launch after upgrade

- [ ] Simulate first launch: delete or set `sessionVisibilityMigrationShown: false` in
      `%LOCALAPPDATA%\CCInfoWindows\settings.json`, then relaunch.
      **Expected:** An informational InfoBar appears at the top of MainView with title and
      message text (DE: "Sitzungssichtbarkeit" / EN: "Session Visibility") describing the
      new 30-day default window.

### 25-2  Toast dismiss persistence (CD-02 crash-safe)

- [ ] With the toast visible (see 25-1), click the X dismiss button.
      **Expected:** The InfoBar closes immediately. Open
      `%LOCALAPPDATA%\CCInfoWindows\settings.json` and verify
      `"sessionVisibilityMigrationShown": true` is written. Relaunch — toast must NOT
      reappear.

### 25-3  Session ComboBox visibility window filter

- [ ] Open Settings → General. Locate the "Sitzungssichtbarkeit" / "Session Visibility"
      ComboBox. Verify it shows 4 options (7 Tage / 30 Tage / 90 Tage / Unbegrenzt) with
      "30 Tage" selected by default.
      **Expected:** Changing the selection immediately re-filters the Active Session ComboBox
      in MainView (session list updates without restart).

---

## Phase 26 — Persistent Session Renaming

**Context:** Users can rename sessions via a pencil button next to the Active Session ComboBox.
Custom names persist to disk and survive restarts. A new "Sessions" Settings tab shows all
sessions and lets users manage names in bulk.

### 26-1  Pencil button opens rename dialog

- [ ] Select a session in the Active Session ComboBox. Verify the pencil button (✏) appears
      to the right of the ComboBox. Click it.
      **Expected:** A ContentDialog opens titled "Sitzung umbenennen" / "Rename session" with
      the current name pre-filled, a text field, Save and Cancel buttons.

### 26-2  Save persists name, ComboBox updates without restart

- [ ] In the rename dialog, clear the name and enter a custom name (e.g. "My Test Session").
      Click Save.
      **Expected:** The Active Session ComboBox immediately shows "My Test Session". Kill and
      relaunch the app — the custom name must still be visible (persisted to
      `%LOCALAPPDATA%\CCInfoWindows\session-names.json`).

### 26-3  Reset button visible only when custom name exists

- [ ] With a custom name set (see 26-2), open the rename dialog again.
      **Expected:** A "Reset" / "Zurücksetzen" button is visible. Click it — the name reverts
      to the auto-derived name. Reopen the dialog — the Reset button is now gone.

### 26-4  Settings Sessions tab visible (5th segment)

- [ ] Open Settings. Verify the Segmented Control shows 5 tabs:
      Allgemein / Aktualisierungen / Konto / Sitzungen / Info (or EN equivalents).
      **Expected:** All 5 tabs fit without truncation at the default window width (360px+).

### 26-5  5-tab Segmented Control fits at 360px width

- [ ] Resize the Settings window to approximately 360px wide.
      **Expected:** All 5 tab labels remain legible and the Segmented Control does not
      overflow or clip. (Verify by eye — exact pixel measurement not required.)

### 26-6  Cross-tab live update: rename in MainView dialog, Settings tab updates

- [ ] With the Settings page open on the Sessions tab, use the pencil button in MainView
      (visible behind Settings) to rename a session to "Live Update Test".
      **Expected:** The Sessions tab in Settings updates the row for that session to show
      "Live Update Test" without requiring a Settings page reload.

### 26-7  Orphan custom names display greyed with subtitle

- [ ] Open Settings → Sessions tab. If any session in `session-names.json` no longer has a
      matching JSONL file (orphan), its row should appear with reduced opacity and a
      subtitle such as "Sitzung nicht gefunden" / "Session not found".
      **Expected:** Orphan rows are visually distinct (greyed/faded) from active session rows.

---

## Phase 27 — NEXTWIN + ORGID + PRICING + L10N

**Context:** Four features: next 5-hour window start label, org-id picker for multi-account
users, pricing error surfacing, and L10N for relative-time text.

### 27-1  NextWindow label visible below 5h-countdown (live API)

- [ ] With an active Claude Code session (live API), observe the 5-hour countdown in MainView.
      **Expected:** Below the "Nächstes Fenster startet um HH:MM" / "Next window starts at HH:MM"
      label is visible when `ResetsAt` is non-null. The time is formatted per the current
      UI locale (DE: 24h / EN: 12h or system format).

### 27-2  NextWindow label hidden when no window data or auth banner shows

- [ ] With no active session OR when the app shows the "Session expired" InfoBar:
      **Expected:** The next-window label is NOT visible (Visibility=Collapsed). It must not
      show a stale timestamp from a previous session.

### 27-3  L10N: LastFetchRelativeTime shows locale-correct text

- [ ] Switch the app language between DE and EN (via Settings → Account or system locale).
      **Expected:** The "Zuletzt aktualisiert vor X Minuten" / "Last updated X minutes ago"
      footer text renders in the active language. "Gerade eben" (DE) / "Just now" (EN) at t=0.

### 27-4  PricingError InfoBar appears when pricing data fails

- [ ] Simulate a pricing fetch failure: disconnect network, wait for the next pricing refresh
      cycle (or restart with network off).
      **Expected:** A warning InfoBar appears in MainView: "Preisdaten nicht verfügbar" /
      "Pricing data unavailable" (or similar). The InfoBar uses the Warning severity style.

### 27-5  PricingError InfoBar disappears on subsequent success

- [ ] With the pricing error InfoBar visible (see 27-4), reconnect network and wait for the
      next refresh cycle.
      **Expected:** The pricing error InfoBar disappears automatically when pricing data loads
      successfully.

### 27-6  PricingError InfoBar suppressed when auth banner shows

- [ ] Force a "session expired" state (let the session time out or clear cookies via
      WebView2 UDF). Verify the main auth InfoBar appears.
      **Expected:** The pricing error InfoBar is NOT visible simultaneously. Only the auth
      InfoBar shows (banner-stack priority: auth > pricing).

### 27-7  Re-detect button on Settings Account tab opens OrgPicker dialog

- [ ] Open Settings → Konto / Account tab. Verify a "Organisation neu erkennen" /
      "Re-detect organization" button is visible below the Logout row.
      **Expected:** Clicking the button opens a ContentDialog listing available organizations
      with Name (bold) and UUID (secondary text).

### 27-8  OrgPicker: Switch triggers logout to LoginView

- [ ] In the OrgPicker dialog (see 27-7), select an organization row and click
      "Wechseln" / "Switch".
      **Expected:** The dialog closes, the app performs a full logout sequence, and the
      LoginView appears. The new org-id is stored in `%LOCALAPPDATA%\CCInfoWindows\claude-org`.

### 27-9  OrgMismatch InfoBar appears after 5 consecutive zero-utilization polls

- [ ] With an active session where usage is genuinely 0% for an extended period (or mock by
      forcing `FetchUsageAsync` to return 0-utilization 5 times), observe MainView.
      **Expected:** After 5 consecutive zero-utilization polls, an "Organisation möglicherweise
      falsch" / "Org ID may be wrong" InfoBar appears with a "Re-resolve" button and a
      "Don't show again this session" checkbox.

### 27-10  "Don't show again" suppresses OrgMismatch InfoBar for current session only

- [ ] With the OrgMismatch InfoBar visible (see 27-9), check the "Don't show again" checkbox.
      **Expected:** The InfoBar disappears and does not reappear during this app session, even
      after more zero-utilization polls. After restarting the app, the suppression resets
      (InfoBar can appear again after 5 zero-polls).

---

## Phase 28 — CLEANUP (automated only)

Phase 28 changes are pure code cleanup with no visible UI impact. Automated verification:

- [x] `dotnet test` — 342/344 pass (2 pre-existing ClaudeApiServiceTests excluded)
- [x] `dotnet build` — 0 errors
- [x] `LogoutRequestedMessage.cs` deleted (CLEANUP-01)
- [x] `_contextModelBadgeColor` initialized to gray-400 via `_brushFactory` seam (CLEANUP-02)
- [x] `MainViewModelInitialStateTests` — 2/2 pass (CLEANUP-02)
- [x] N-1/N-2/N-3 nits removed (CLEANUP-03)
- [x] G-3 convention documented in `CLAUDE.md` (CLEANUP-04)

---

## Sign-off

| Section | Items | Status |
|---------|-------|--------|
| Phase 24 (automated) | 1 | [x] auto |
| Phase 25 | 3 | [ ] pending |
| Phase 26 | 7 | [ ] pending |
| Phase 27 | 10 | [ ] pending |
| Phase 28 (automated) | 6 | [x] auto |
| **Total** | **27** | **pending visual sign-off** |

When all Phase 25-27 items are checked, milestone v1.5 is complete.

---

_Generated: 2026-05-08_
_Phase: 28-v1-4-cleanup-final-uat_
