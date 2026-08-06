# Domain Pitfalls — v1.5 macOS v1.12.0 Feature Parity + Hardening

**Domain:** WinUI 3 / .NET 9 desktop app — adding 11 items to existing CCInfoWindows
**Researched:** 2026-05-07
**Scope:** Pitfalls SPECIFIC to the v1.5 surface — already-solved pitfalls listed in `<milestone_context>` are NOT repeated here.

---

## Cluster A Pitfalls — macOS v1.12.0 Feature Parity (A1, A2)

### A1-P1: Cross-day weekday formatting collapses to current weekday — user reads "Mi., 14:30" assuming today

- **Symptom:** Reset is at 14:30 next Wednesday; the label says "Mi., 14:30" while the clock reads 18:00 today (Wednesday). User assumes window resets in 3.5h, panics about burn-rate. Or: at 23:55 the reset is at 00:30 the same date+1, label reads "Do., 00:30" but the day-of-week portion is the only disambiguator and is easy to miss.
- **Root cause:** `data.FiveHour.ResetsAt.LocalDateTime.ToString("ddd HH:mm", culture)` produces ambiguous output when reset is >24h away (same weekday name reappears) or just past midnight (no date hint). The existing pattern in `CountdownFormatter.FormatResetDate` (used by weekly windows, see `MainViewModel.cs:531`) was designed for 7-day windows where weekday alone is unambiguous; the 5-hour window almost always lands today or tomorrow but Storm/sleep/timezone-shift edge cases break the assumption.
- **Prevention:**
  - Format-switching rule based on `(reset - now).TotalHours`:
    - `< 12h && same calendar day` → `"HH:mm"` only (e.g. `"22:30"`)
    - `< 24h && next calendar day` → `"morgen HH:mm"` / `"tomorrow HH:mm"` (localized resw key)
    - `>= 24h` → `"ddd dd.MM. HH:mm"` (weekday + date, e.g. `"Mi 8.5. 14:30"`)
  - Compute the comparison in `LocalDateTime` to avoid TZ-offset jumps producing false "tomorrow" classifications.
  - Add resw keys: `NextWindowTodayFormat`, `NextWindowTomorrowFormat`, `NextWindowFutureFormat` (DE+EN both).
    **Corrected 2026-08-06:** these three were originally written here as `NextWindowToday.Format` /
    `NextWindowTomorrow.Format` / `NextWindowFuture.Format`. WinUI3Localizer 2.3.0 splits a resw key at the
    FIRST dot and treats the left half as a target element name, so a dotted key resolves to an empty string
    with no error — the exact trap M2-P1 below warns about, recommended here by accident. Every resw key must
    be single-segment except the `[using:...]` attached-property form. `ResourceCoverageTests` enforces this
    and also asserts every `<data name>` is resolvable. Shipped reality: Phase 27 used the two single-segment
    keys `NextWindowLabelDe` / `NextWindowLabelEn`.
  - Unit-test with `FakeClock` at five boundaries: now=23:50/reset=00:20 same day, now=23:50/reset=00:20 next day, now=10:00/reset=15:00 same day, now=10:00/reset=15:00 next day, now=10:00/reset=10:30 in 7 days (DST jump).
- **Phase that should address:** A1 (Next 5h-window start time label). The format helper should live in `Helpers/CountdownFormatter.cs` (extend, do not duplicate) so weekly/Sonnet labels can later opt into the same rule.

### A1-P2: ResetsAt null on cold start renders "--" but XAML hides the entire label row, breaking layout shift

- **Symptom:** Cold-start before first poll, `_fiveHourResetsAt == null`. If the new label uses `Visibility` binding tied to nullable, the row collapses, then re-expands ~3 seconds later when the first poll lands — visible layout pop on every launch.
- **Root cause:** WinUI 3 layout doesn't reserve space for `Visibility="Collapsed"` rows. `MainViewModel.FiveHourCountdown = "--"` (line 491) keeps the countdown row stable because it always has a string value; if the new "next window starts at" row uses a separate Visibility flag, it desyncs from countdown.
- **Prevention:**
  - Use a string-based fallback identical to `FiveHourCountdown`: introduce `[ObservableProperty] string _fiveHourNextWindowText = "--";` and bind directly. Never use a separate Visibility flag for this row.
  - When `_fiveHourResetsAt == null`, set `FiveHourNextWindowText = "--"` (same convention as `FiveHourCountdown` line 491). The row stays in the layout tree.
  - If marketing wants the label hidden until data lands, use `Opacity=0` + `IsHitTestVisible=False` to preserve layout space.
- **Phase that should address:** A1 — XAML wiring step. Mirror the existing countdown pattern; do not invent a new visibility convention.

### A2-P1: Session-rename JSON I/O race — concurrent rename + JsonlService.RebuildSessionsList drops the rename

- **Symptom:** User renames a session "MyProject", clicks save, sees the new name briefly, then within 2 seconds the dropdown reverts to the old name (or shows the directory basename). After app restart the rename is gone.
- **Root cause:** Rename writes a new mapping to `%LOCALAPPDATA%\CCInfoWindows\session-names.json` (proposed). Meanwhile, `JsonlService.OnFileChanged` (line 828) fires from the FileSystemWatcher 2s debounce, calls `ProcessPendingFileChanges` (line 856), which calls `RebuildSessionsList` (line 779) under `_sessionsLock`. If `RebuildSessionsList` reads `session-names.json` and the rename writer is mid-flight (between `File.Delete` and `File.WriteAllText`), the read returns either old data or `FileNotFoundException`, which the rebuild swallows, falling back to `SessionNameHelper.GetDisplayName` (line 785).
- **Prevention:**
  - Mirror the v1.4 `UsageHistoryService` pattern (`UsageHistoryService.cs:26`): introduce a `SessionNameStore` service with a `private readonly SemaphoreSlim _writeLock = new(1, 1);` serializing all reads and writes. Both the rename command (UI thread) and `RebuildSessionsList` (debounce timer thread) acquire the same semaphore.
  - Use `await _writeLock.WaitAsync()` in async paths; never use `lock` keyword (cannot hold across `await` — the comment at `UsageHistoryService.cs:25` already states this).
  - Use atomic-rename-on-write: write to `session-names.json.tmp`, then `File.Move(tmp, final, overwrite: true)` — File.Move is atomic on NTFS for same-volume moves. This way readers without the semaphore still never see a partial file.
  - Cache the latest written snapshot in-memory (mirror `_lastSavedSnapshot` at `UsageHistoryService.cs:29`) so `RebuildSessionsList` doesn't disk-read on every debounce tick.
  - Custom names live OUTSIDE `_projectData` (it's rebuilt from JSONL on every cold start). Apply the rename overlay in `RebuildSessionsList` BEFORE the `OrderByDescending` so display order remains by activity but DisplayName reflects the override.
- **Phase that should address:** A2 — Session rename + persistence. Implement `ISessionNameStore` first; wire the overlay in `JsonlService.RebuildSessionsList` second.

### A2-P2: Unicode session names break filesystem index when keyed by name

- **Symptom:** User renames a session to "Émoji 🚀 / Backup" — save appears to work, but on restart the rename is gone, or worse: a different session takes on the emoji name.
- **Root cause:** If `session-names.json` keys by display name instead of `SessionInfo.Id` (the project directory name from `JsonlService:508`), Unicode normalization, path separators (`/`, `\`), and case-folding all bite. Windows filesystem is case-insensitive but JSON keys are case-sensitive.
- **Prevention:**
  - Key the rename store by `SessionInfo.Id` (the project directory name — already unique, ASCII-safe by Claude Code's encoding). Treat it as opaque.
  - Validate user input on rename: reject control chars, NUL, length >100, trim whitespace, but ALLOW emoji and full Unicode in the value (only the key needs to be safe).
  - Persist as `Dictionary<string, string>` (Id → CustomName), serialize with `JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }` so emojis don't get `\uXXXX`-escaped (smaller files, easier to inspect manually).
- **Phase that should address:** A2 — Session rename. Validate early, normalize never.

### A2-P3: Cross-tab live-update — rename in Settings tab doesn't refresh dropdown in MainView

- **Symptom:** User opens Settings → Sessions tab, renames a session, navigates back to MainView. Dropdown still shows the old name until the next FileSystemWatcher debounce fires (could be minutes).
- **Prevention:**
  - DO NOT use a `SessionRenamedMessage` via `WeakReferenceMessenger.Default` for this — the documented pitfall (`memory/architecture_weakreferencemessenger_with_transient_vms.md`) applies: `MainViewModel` is `AddTransient`, will be GC'd when navigated away from, registration vanishes silently. SettingsViewModel is also AddTransient. The roundtrip is exactly the failure mode that broke logout in v1.4.
  - Instead: inject `ISessionNameStore` into both VMs. Have it expose `event EventHandler? NamesChanged;`. SettingsViewModel calls `_store.Rename(id, name)` which raises the event. MainViewModel subscribes in `InitializeAsync` and unsubscribes in `StopTimers`. Since `ISessionNameStore` is a `Singleton` it never gets GC'd; events on singletons survive transient consumers (the consumer's lifetime governs subscription, not the publisher's).
  - The handler in MainViewModel must marshal to UI thread: `_dispatcherQueue?.TryEnqueue(RefreshSessionList)` — same pattern as `Receive(SessionTimeoutChangedMessage)` at line 1032.
- **Phase that should address:** A2 — Settings "Sessions" tab wiring. Cite this pitfall in the wave that touches `App.xaml.cs` DI registration: `services.AddSingleton<ISessionNameStore, SessionNameStore>();`

---

## Cluster B Pitfalls — Bug Hardening (B1, B2, B3)

### B1-P1: Cold-start scan vs. file-watcher debounce — JSONL written during DiscoverSessions iteration

- **Symptom:** User launches app, Claude Code is actively writing to a session JSONL (every few seconds). On cold start, `DiscoverSessions` iterates `Directory.GetFiles` (line 509) and reads each file via `ReadAllLines` (line 433). If a file is written between `GetFiles` enumeration and `ReadAllLines`, `_filePositions[filePath]` stores `endPos` (line 444) — but the file already has more bytes than that endPos. Subsequently, the FileSystemWatcher Changed event fires for the same file, `ProcessPendingFileChanges` (line 856) calls `ProcessSingleFile` (line 890) → `ParseFileIntoProject` with `forceFullRead: false` — the incremental read picks up only bytes since the cached position, but the cached position is already AHEAD of some lines that were written during the cold-start read. **Lost lines**, never counted.
- **Root cause:** `ReadAllLines` (line 433) opens with `FileShare.ReadWrite` so it doesn't block the writer; `stream.Length` at line 444 is sampled after the read but Claude Code may have appended bytes that ended up in the StreamReader buffer (already read) OR after stream.Length capture (not read, but position now claims they were). Worse: incremental reader at line 451 trusts the cached position absolutely.
- **Prevention:**
  - Re-read `stream.Length` AFTER consuming all lines (or use `FileStream.Position` of the underlying stream after StreamReader exhausts). The current code at line 444 returns `stream.Length` which is sampled as a snapshot but `StreamReader` may have buffered bytes beyond that. Switch to `stream.Position` after the final `ReadLine` returns null — that's the byte offset of the last consumed line terminator.
  - During cold-start while `_isScanning == 1`, `ProcessPendingFileChanges` already short-circuits (line 859). Verify the inverse: when scan completes, drain the `_pendingChangedFiles` set (which may have accumulated during the scan) and reprocess each one. Currently the watcher is started AFTER the scan (`StartWatching` at line 247), so events written during the scan are LOST entirely. Two fixes:
    1. Start the watcher BEFORE `DiscoverSessions` so events queue into `_pendingChangedFiles` during the scan; `ProcessPendingFileChanges` then runs after the scan completes.
    2. After scan completes and watcher starts, do one more sweep: `_pendingChangedFiles.UnionWith(Directory.GetFiles(_projectsDirectory, JsonlFilePattern, SearchOption.AllDirectories))` to force-recheck every file (redundant but safe; deduplication guards against double-counting independently of the cached position).
  - Deduplication is the source of truth for double-count protection — even if the position cache is wrong, re-reading the same lines must not change any total. **Corrected 2026-08-06:** the `SeenIds`/`UniqueHash` design this pitfall originally described never existed in shipped code (Claude Code writes no `uniqueHash` field). The current design is `ProjectData.EntryIndexByKey`, keyed on `message.id|requestId` with the per-line `uuid` as fallback, mapping an identity to its index in `ProjectData.EntryLog`. A repeated identity **supersedes** the earlier entry in place rather than being skipped, because Claude Code writes one line per streamed content block and only the last one carries the completed `output_tokens` — so first-seen-wins would freeze a partial count. `EntryLog` is the only place a contribution is recorded (there are no parallel running totals), which is what makes a re-read idempotent instead of additive.
- **Phase that should address:** B1 — Cwd hydration + visibility-window filter. The visibility-window filter is the user-facing change; the position-vs-watcher race is the lurking bug that the visibility filter would NOT fix and might surface (a session written during cold-start that gets visibility-filtered for being "too old" never gets its tokens counted).

### B1-P2: Visibility-window setting changes don't reactively re-filter the dropdown

- **Symptom:** User in Settings switches "Show sessions from last X days" from 30 to 7. Dropdown still shows 30-day-old sessions until next FileSystemWatcher debounce or app restart.
- **Root cause:** Same architectural family as `Receive(SessionTimeoutChangedMessage)` (line 1028). When the threshold lives in `AppSettings`, only the components that read settings on demand pick up the change. `RebuildSessionsList` at line 779 doesn't read settings at all — it iterates `_projectData` unconditionally. The filter must be added there.
- **Prevention:**
  - Add a new message: `SessionVisibilityWindowChangedMessage` (broadcast — Messenger is fine here per `architecture_weakreferencemessenger_with_transient_vms.md` rule for non-exactly-once flows).
  - Register in `MainViewModel.InitializeAsync` mirroring the pattern at line 318 (`RefreshIntervalChangedMessage`). Handler calls `_dispatcherQueue?.TryEnqueue(RefreshSessionList)` — UI-thread marshaling required (see Cross-Cluster G-1 below).
  - In `JsonlService.RebuildSessionsList` (line 779), inject the cutoff: `var visibilityWindow = _settingsService?.LoadSettings().SessionVisibilityWindowDays ?? DefaultVisibilityDays;` — then add `.Where(s => s.LastActivity >= DateTimeOffset.UtcNow.AddDays(-visibilityWindow))` to the filter chain. `_settingsService` already exists (line 85) and is null-tolerant.
  - Special-case "unlimited" → skip the filter entirely (don't pass `int.MaxValue` and rely on `AddDays` not overflowing).
- **Phase that should address:** B1 — Visibility-window filter wave.

### B2-P1: Stale-cached-org-id false positive — heuristic mistakes legitimate idle for "wrong org"

- **Symptom:** User on vacation returns, opens app. Has had genuinely zero usage for 5 days. The "5 consecutive zero polls" heuristic fires, app prompts "Detected possible org mismatch — re-resolve?" — user clicks yes, app navigates to login, re-resolves. Wastes 30 seconds and creates a moment of "did I just lose my session" panic.
- **Root cause:** A binary `is-zero-utilization` heuristic over a fixed window of polls cannot distinguish "user has been idle" from "wrong org cached". Both produce identical API responses (200 with `utilization: 0`).
- **Prevention:**
  - Confidence-threshold UX, NOT auto-switching:
    - Track `_consecutiveZeroPolls` separately from history.
    - When `_consecutiveZeroPolls >= 10` AND `IsBurnRateWarningVisible == false` AND `UsageHistoryPoints` shows zero variance for the entire 5-hour window AND the system has had non-idle input (best-effort: if `MainWindow.Visibility == Visible` for >5min) → soft-prompt via InfoBar (NOT a modal): "No usage detected for [duration]. If you're using Claude Code, you may have multiple Anthropic orgs. [Re-resolve org]."
    - User-dismissable; remembers dismissal for the session via `_dismissedOrgMismatchHint = true`.
    - NEVER auto-navigate or auto-switch; require explicit click.
  - Add an explicit "Force re-resolve org" button in Settings → Account tab — that's the true "I know what I want" path. Heuristic is a hint, button is a hammer.
  - Detection on the wire: when `TryMigrateOrgIdAsync` (referenced in `architecture_weakreferencemessenger_with_transient_vms.md:62-63`, currently picks `orgs[0]` blindly) is called via the explicit button, present `orgs` as a picker if `orgs.Count > 1`. If exactly one org and zero usage persists, the user genuinely has zero usage — show a localized status hint, not a banner.
  - Cookie-vs-cached-org mismatch detection (deterministic, NOT heuristic): compare `cookies.sessionKey`'s embedded user ID (if Anthropic exposes it; if not, hash the cookie value) against the user-id stored alongside the cached org-id. If hashes diverge → auto-clear `claude-org` and force re-resolve on next poll. This is a hard signal, not a probabilistic one.
- **Phase that should address:** B2 — Org-ID picker. Implement explicit-button path FIRST (hard signal, deterministic). Add heuristic banner LAST (soft signal, dismissable). Document the false-positive trade in the ADR / Key Decisions table of `PROJECT.md`.

### B2-P2: Org switch leaves WebView2 cookies stale — picker writes new org-id but next poll uses old cookie

- **Symptom:** User picks a different org from the picker. App updates `claude-org` in Credential Manager, restarts polling. API still returns the old org's data because the WebView2 cookie jar (`%LOCALAPPDATA%\CCInfoWindows\WebView2`) still holds the session for the previous org.
- **Root cause:** The Cloudflare WebView2 bridge (documented in `MEMORY.md`) routes API calls through the embedded browser's cookie jar. Changing `claude-org` in Credential Manager doesn't touch cookies. If org switching requires a fresh login (cookie sessionKey is per-org-context), the picker must trigger a logout-equivalent flow.
- **Prevention:**
  - Document the contract: switching orgs requires re-authentication. The picker UI should warn: "Switching to [Other Org] requires re-login." Confirm dialog → call same logout sequence as `MainViewModel.Logout` (line 936) (clear creds, `_bridge.Reset()`, navigate to LoginView).
  - Alternative if Anthropic supports multi-org-per-session: send the org-id as a request header in the WebView2 fetch call instead of relying on cookie context. Verify with Anthropic API behavior before assuming. (Tauri reference at `D:\myProjects\ccInfoWindows\src\services\claude-api.ts` may show this.)
  - DO NOT silently re-call `_bridge.Reset()` without telling the user — they'll lose context window and statistics state immediately and not understand why.
- **Phase that should address:** B2 — Org-ID picker (UX wave). The "switch requires re-login" contract is a UX decision that needs to land in styleguide / spec, not just code.

### B3-P1: Pricing-banner spam — every poll re-triggers banner on persistent failure

- **Symptom:** Network down for 20 minutes. Pricing banner appears, then disappears after 12-hour cache fallback, then on next poll the banner re-appears. User sees flicker every poll cycle.
- **Root cause:** If the banner is driven by `HasApiError` mirroring (proposed in `backlog_pricing_never_loaded.md`), `_pricingService.EnsurePricesLoadedAsync()` failure on every `AggregateStatisticsAsync` (line 800) flips the flag rapidly. Existing `HasApiError` for usage-fetch failures (line 430) already coexists — two error sources writing to the same banner property cause flicker.
- **Prevention:**
  - Separate banner: `[ObservableProperty] bool _isPricingError;` distinct from `_hasApiError`. Distinct InfoBar control in XAML, stacks visually below the auth-error InfoBar (`IsSessionExpired`) and the burn-rate banner (`IsBurnRateWarningVisible`). All three are mutually independent.
  - Set `IsPricingError` only after N consecutive failures (e.g. 3) — single transient blip doesn't show banner. Track `_pricingFailureCount` int, increment in catch, reset to 0 in success.
  - Once shown, banner persists until next successful fetch. Don't auto-dismiss on a single retry succeeding either; use `_pricingFailureCount = 0` only after a fully successful `EnsurePricesLoadedAsync` returns.
  - Localize banner text via resw key `PricingError.Text` (DE+EN). Couples with M-2 (LastFetchRelativeTime localization) — same surface, same wave.
  - Existing fire-and-forget at `MainViewModel.cs:371-375` swallows the exception entirely. Replace with: catch sets `_pricingFailureCount++`, if threshold crossed `_dispatcherQueue?.TryEnqueue(() => IsPricingError = true)` (UI marshal — this fire-and-forget runs on `Task.Run` thread pool, so marshaling is mandatory).
- **Phase that should address:** B3 — Pricing banner. The `Task.Run` at line 371 needs the marshaling fix at the same time (same code path as C-2).

### B3-P2: Banner conflicts with existing burn-rate / auth banners — UI gets crowded

- **Symptom:** All three banners visible simultaneously: burn-rate warning + auth-expired InfoBar + pricing-error banner. Header collapses into 80px of warnings, dashboard pushed below the fold.
- **Prevention:**
  - Stack order convention (most-blocking first):
    1. `IsSessionExpired` — actionable, blocks all data
    2. `IsPricingError` — affects cost numbers only
    3. `IsBurnRateWarningVisible` — informational
  - Cap visible banners at 2 simultaneously: if `IsSessionExpired == true`, suppress `IsPricingError` rendering (auth fix re-triggers pricing fetch anyway). Implement via XAML converter `MultiBannerVisibilityConverter` or simple `x:Bind` boolean expressions.
  - Use `InfoBar` (built-in WinUI 3) not custom Border — it handles severity colors, dismiss button, and has consistent height (44px) so layout math is predictable.
- **Phase that should address:** B3 — Pricing banner XAML wiring. Spec the stack order as a Decision in `PROJECT.md` Key Decisions table.

---

## Cluster C Pitfalls — v1.4 Code-Review Remediation (C-1, C-2, M-1/2/3, Nits)

### C1-P1: try/catch wrapping `RefreshCommand.ExecuteAsync(null)` is still fire-and-forget — exceptions logged but no user feedback

- **Symptom:** Post-login `Receive(AuthStateChangedMessage(true))` calls `RefreshCommand.ExecuteAsync(null)` (line 1008). If the underlying `Refresh` task throws, the exception is awaited internally by `ExecuteAsync` (sets `IRelayCommand.IsRunning` back to false) but no callback to MainViewModel surfaces the failure — user sees stale data, no error banner.
- **Root cause:** `IAsyncRelayCommand.ExecuteAsync` returns `Task` but the comment at line 1006-1007 explicitly accepts fire-and-forget because `Receive` is `void`. Wrapping in try/catch around `ExecuteAsync(null)` doesn't help because exceptions inside the underlying `Refresh` method are caught by `[RelayCommand]`'s machinery first — they never propagate out. The try/catch at the call site is dead code.
- **Prevention:**
  - The real fix: ensure `Refresh()` (line 906) sets `HasApiError` / `ApiErrorMessage` on failure (it already does via `PollUsageCoreAsync` at line 428-458). No outer try/catch needed.
  - For genuine fire-and-forget (`_ = Task.Run(...)` at line 371), use the pattern:
    ```csharp
    _ = Task.Run(async () => {
        try { await _pricingService.EnsurePricesLoadedAsync(); }
        catch (Exception ex) {
            AppLog.Write("MainViewModel.PricingLoad", ex);
            _dispatcherQueue?.TryEnqueue(() => /* set IsPricingError per B3-P1 */);
        }
    });
    ```
    **Corrected 2026-08-06 (review finding 34):** the snippet above originally logged via
    `Debug.WriteLine`. That carries `[Conditional("DEBUG")]`, so the compiler erases it from the Release
    build users run — a catch body whose only statement is `Debug.WriteLine` is an *empty* catch body in
    production, which is exactly the silent-failure class this pitfall is trying to prevent.
    `CCInfoWindows.Helpers.AppLog` is the sink: `AppLog.Write(source, ex)` appends to
    `%LOCALAPPDATA%\CCInfoWindows\app.log` (1 MiB, single roll), never throws, thread-safe, works before
    the DI container exists. Second correction: this particular catch is unreachable —
    `EnsurePricesLoadedAsync` cannot throw because every loader inside `LiteLLMPricingService` catches
    internally, so `IsPricingError` has to be driven off `IPricingService.Source`, not off an exception.
  - DON'T add `CancellationToken` plumbing for these fire-and-forget paths just because OWASP says you should — this app's polling/refresh is naturally bounded by `_pollTimer.Interval`. Cancellation tokens are for `AggregateStatisticsAsync` (already has one at line 776) where the user can switch tabs and need stale work cancelled.
  - For `RefreshCommand.ExecuteAsync(null)` at line 1008: change to `_ = RefreshCommand.ExecuteAsync(null)` with the discard `_` to make fire-and-forget intent explicit. The comment already explains why; the discard documents at the call site.
- **Phase that should address:** C-1 — Fire-and-forget exception swallow remediation. Document "what fire-and-forget means in this codebase" in CLAUDE.md or an ADR.

### C2-P1: Naive `if (!HasThreadAccess) TryEnqueue else inline` masks reentrancy bugs

- **Symptom:** A `Receive(AuthStateChangedMessage)` handler refactored with the proposed pattern works in tests, but in production a chained `Receive` (e.g., `Receive(true)` triggers `RefreshCommand.ExecuteAsync(null)` → `PollUsageCoreAsync` → eventually another `WeakReferenceMessenger.Send(AuthStateChangedMessage(false))` if 401 returns → recursive `Receive(false)` on UI thread) — the inline branch executes the recursive handler synchronously inside the parent handler's stack frame. Stack overflow, or worse, mid-update inconsistent state (e.g. `IsSessionExpired = true` while `_autoReauthAttempted` is still `false` in the parent frame).
- **Root cause:** `if (!DispatcherQueue.HasThreadAccess) DispatcherQueue.TryEnqueue(action) else action()` — the "else" branch runs synchronously, allowing recursive Send → Receive chains to execute inside each other. `WeakReferenceMessenger.Send` is synchronous (`memory:55-56`).
- **Prevention:**
  - Always `TryEnqueue`, even when on UI thread. `DispatcherQueue.TryEnqueue` posts to the queue; if you're already on the UI thread, the action runs on the NEXT message-loop turn, not synchronously. This breaks re-entrancy: the parent `Receive` finishes its work, then the child `Receive` work runs cleanly.
    ```csharp
    public void Receive(AuthStateChangedMessage message)
    {
        _dispatcherQueue?.TryEnqueue(() => HandleAuthStateChangedCore(message));
    }
    private void HandleAuthStateChangedCore(AuthStateChangedMessage message) { /* existing body */ }
    ```
  - Cost: a one-message-loop delay before the handler runs. Acceptable for auth-state changes (already has a loading state from the in-flight HTTP request). NOT acceptable for high-frequency messages like chart-invalidate (unmeasurable to humans, but profile if uncertain).
  - This forces the same pattern as `Receive(SessionTimeoutChangedMessage)` at line 1032 — already correct. C-2 just brings `Receive(AuthStateChangedMessage)` (line 997) into line.
  - For testability: introduce `IDispatcherQueue` interface mirroring `IDispatcherTimer` (`Services/Interfaces/IDispatcherTimer.cs`). Production wraps `DispatcherQueue.GetForCurrentThread()`; tests supply `FakeDispatcherQueue` that runs actions immediately OR queues them based on the test scenario. This unblocks unit tests for the off-thread Receive path that AUTH-01/02 visual smoke deferred (`PROJECT.md:117`).
- **Phase that should address:** C-2 — DispatcherQueue marshaling. The `IDispatcherQueue` adapter is the v1.5 deliverable; the always-TryEnqueue rule is the policy.

### C2-P2: `_dispatcherQueue` field is null until `InitializeAsync` runs — Receive on cold path crashes

- **Symptom:** App starts, login completes, before `MainView.Loaded` fires `_dispatcherQueue` (line 69) is still null. `WeakReferenceMessenger.Default.Send(AuthStateChangedMessage(true))` from LoginViewModel triggers `Receive` → `_dispatcherQueue?.TryEnqueue(...)` — the null-conditional silently no-ops. Logout flow appears to work, but post-login refresh never fires.
- **Root cause:** `_dispatcherQueue` is assigned at `InitializeAsync` line 311. Constructor-time registration at line 301-302 happens BEFORE Initialize. Window between constructor and Loaded is a hole.
- **Prevention:**
  - Initialize `_dispatcherQueue` in the constructor: `_dispatcherQueue = DispatcherQueue.GetForCurrentThread();` — but only if the constructor is invoked on the UI thread (verify: `App.xaml.cs` DI resolution path). If DI runs on non-UI thread (rare, but possible), the constructor call returns null and you're back to square one.
  - Safer: lazy-resolve at first use:
    ```csharp
    private DispatcherQueue ResolveDispatcher() =>
        _dispatcherQueue ??= DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("DispatcherQueue unavailable — must be called from UI thread context.");
    ```
  - For tests: when using `FakeDispatcherQueue`, inject via constructor parameter (same pattern as `IDispatcherTimer`). This forces the DI registration to be explicit and removes the cold-path null risk.
- **Phase that should address:** C-2 — DispatcherQueue marshaling. Combined with `IDispatcherQueue` adapter introduction.

### C2-P3: Re-registering messages in `InitializeAsync` causes double-fire if InitializeAsync runs twice

- **Symptom:** User logs out (clears state), logs back in, `InitializeAsync` called again on the new MainViewModel. `WeakReferenceMessenger.Default.Register<RefreshIntervalChangedMessage>(this, ...)` (line 318) registers a SECOND handler on the same recipient instance. Settings-change → handler fires twice → refresh interval set twice (idempotent here, but the pattern is dangerous).
- **Root cause:** `WeakReferenceMessenger` allows multiple registrations of the same recipient + message type (it's a list, not a set). `IRecipient<T>` interface registrations (line 301-302) are deduplicated; lambda registrations (line 318, 324) are NOT.
- **Prevention:**
  - At the top of `InitializeAsync`: `WeakReferenceMessenger.Default.UnregisterAll(this);` — paired with re-registration. Idempotent.
  - OR: convert lambda registrations to `IRecipient<RefreshIntervalChangedMessage>` interface implementations on MainViewModel, which dedupe by recipient identity.
  - OR: gate `InitializeAsync` with a bool `_isInitialized` that throws on second call (forces explicit handling in callers).
- **Phase that should address:** C-2 cleanup wave OR a Nit. Low severity but easy to forget when adding A2's `ISessionNameStore.NamesChanged` subscription.

### M2-P1: Localization regression — new `LastFetchRelativeTime` resw keys collide with existing keys

- **Symptom:** Dev adds `LastFetch.Never` and `LastFetch.Format` to both `de-DE` and `en-US` resw, but mistypes the German one as `LastFetch.Nie`. UI shows the literal key string instead of localized text.
- **Root cause:** `WinUI3Localizer` resolves keys silently — missing key returns empty string or key literal depending on config. No compile-time check. v1.4 added `ResourceCoverageTests` (`PROJECT.md:45`) that validates 6 L10N-01 keys × 2 locales structurally — extend it.
- **Prevention:**
  - Add new keys to `ResourceCoverageTests` enumeration. Test runs on every build; missing or mismatched key → red.
  - Naming convention: **single-segment PascalCase with the cluster as a prefix**, e.g. `LastFetchNever`,
    `LastFetchMinutesAgo`. **Corrected 2026-08-06:** this line originally prescribed `Cluster.Component.State`
    (`Pricing.LastFetch.Never`). Dots are not a namespace separator here — WinUI3Localizer 2.3.0 splits at the
    first dot and reads the left half as a target element name, so every dotted key silently resolves to an
    empty string. Shipped reality: `LastFetchNever` / `LastFetchJustNow` / `LastFetchMinutesAgo` /
    `LastFetchHoursAgo` / `LastFetchDaysAgo`. Long prefixes, not dots, are how you avoid collisions.
  - Use `Localizer.Get().GetLocalizedString(key)` inside a try/catch with a fallback (mirror the pattern at `MainViewModel.cs:638-644`) — defensive against missing keys at runtime.
- **Phase that should address:** M-2 — Localize hardcoded EN strings in `LastFetchRelativeTime`. Couples with B3-P1 (same surface).

### M3-P1: Default `_contextModelBadgeColor = null!` masks XAML binding errors at startup

- **Symptom:** Dev adds a new context-model branch that doesn't call `ContextModelBadgeColor = ParseHexBrush(...)`. App starts, badge shows null brush, XAML throws "value cannot be null" — but the `null!` declaration suppressed the compile-time warning that would have caught it.
- **Root cause:** `null!` (line 213) is a "trust me, it'll be set" annotation. `ClearSessionData` (line 750-762) DOES set it (line 756 → `ParseHexBrush(ModelContextLimits.GetBadgeColorHex(null))`). But the field is mutated by multiple methods; any new path that forgets the assignment ships a null brush.
- **Prevention:**
  - Initialize at field declaration with the same default as `ClearSessionData`:
    ```csharp
    [ObservableProperty]
    private SolidColorBrush _contextModelBadgeColor = ParseHexBrush(ModelContextLimits.GetBadgeColorHex(null));
    ```
    But `ParseHexBrush` is a static method on the same class and field initializers run before the constructor — verify this works (it should, ParseHexBrush has no instance dependencies).
  - Alternative: lazy property pattern — make `ContextModelBadgeColor` a derived property computed from `_contextModelName` (a primitive string field).
  - Add a `Debug.Assert(value is not null, "...")` inside the partial setter via `OnContextModelBadgeColorChanging` — catches in debug builds only.
- **Phase that should address:** M-3 — Restore real default for `_contextModelBadgeColor`. Verify `ParseHexBrush` invocation order works at field-initializer time.

---

## Cross-Cluster Pitfalls (always-on rules)

### G-1: Every new `IRecipient<T>` (or lambda message handler) MUST follow the C-2 marshaling rule

- **Symptom:** A1's optional `FiveHourResetTimeChangedMessage` (if introduced) or A2's `SessionRenamedMessage` (if introduced) or B1's `SessionVisibilityWindowChangedMessage` works in unit tests but flickers in production because some senders are on `Task.Run` thread pool.
- **Root cause:** `WeakReferenceMessenger.Send(...)` runs receivers synchronously on sender's thread (`memory/architecture_weakreferencemessenger_with_transient_vms.md:55`). UI mutations off-thread are silent corruption.
- **Prevention:**
  - **Codebase rule:** every `Receive(...)` method that touches `[ObservableProperty]`, navigation, or XAML controls MUST start with `_dispatcherQueue?.TryEnqueue(() => HandleCore(...));` — no exceptions.
  - Add this rule to `CLAUDE.md` under MVVM Conventions (currently silent on this).
  - Add a `MessengerMarshalingTests` test class that registers fakes for each `IRecipient<>` MainViewModel implements, sends from a `Task.Run` thread, and asserts the handler executed via `FakeDispatcherQueue` (not synchronously).
- **Phase that should address:** C-2 establishes the pattern; A2/B1 must follow. Roadmapper should add an explicit success criterion to A2/B1 phases: "verify all new IRecipient<> handlers route through FakeDispatcherQueue in tests."

### G-2: Every new JSON-on-disk store MUST use the `UsageHistoryService` SemaphoreSlim pattern

- **Symptom:** A2's session-name store, B2's org-id picker preferences, anything that writes to `%LOCALAPPDATA%\CCInfoWindows\*.json` outside a single-writer thread risks the same race that v1.4 hardened `UsageHistoryService` against (`UsageHistoryService.cs:25-29`).
- **Prevention:**
  - **Codebase rule:** new persistence services follow the `IUsageHistoryService` shape:
    1. `private readonly SemaphoreSlim _writeLock = new(1, 1);`
    2. Sync + async write methods (`Save`, `SaveAsync`) — sync needed for `Window.Closed` flush, async for live updates
    3. `private TStore? _lastSavedSnapshot` cache for read-without-disk-hit
    4. `Peek*` method for atomic snapshot read (no lock needed — atomic reference assignment)
    5. Atomic-rename via `tmp + File.Move` for crash-safety
  - Add this convention to `CLAUDE.md` Project Structure section as a sub-rule under `Services/`.
  - Avoid the `lock` keyword for any I/O — `SemaphoreSlim` only (the comment at `UsageHistoryService.cs:25` already asserts this; codify it).
- **Phase that should address:** A2 (session-name store) is the FIRST consumer; codify the rule there so B2's settings persistence (if it adds new files) follows.

### G-3: Every new `[ObservableProperty]` field with default `null!` MUST have an initializer or `ClearXxx()` invocation in the constructor path

- **Symptom:** Same as M3-P1, generalized. Adding A1's "next window text" or B3's "pricing error message" with `null!` and forgetting to initialize causes XAML null-binding crashes only when that code path runs (e.g., before first poll).
- **Prevention:**
  - **Codebase rule:** prefer `= string.Empty;`, `= "--";` (sentinel), or `= ParseHexBrush(...)` field initializers over `null!`.
  - When `null!` is unavoidable (rare — usually only for DI-injected services that the framework guarantees), document with a `// non-null after constructor — set by Foo()` comment.
  - Roslyn analyzer rule (future): flag `null!` on `[ObservableProperty]` private backing fields. Out of v1.5 scope, but worth a backlog entry.
- **Phase that should address:** M-3 — sets the precedent. Document in `CLAUDE.md` under MVVM Conventions.

---

## Top 3 Watch-Outs (for SUMMARY.md synthesizer)

1. **The C-2 marshaling rule applies to A2 and B1 too.** Adding `ISessionNameStore.NamesChanged`, `SessionVisibilityWindowChangedMessage`, or any A1 reset-time-changed signal without `_dispatcherQueue?.TryEnqueue(...)` will silently break in production exactly the same way `Receive(AuthStateChangedMessage)` did — and unit tests on the UI thread won't catch it. Roadmapper: add anti-regression criterion "all new IRecipient<>/event handlers route through FakeDispatcherQueue in tests" to A2 + B1 phases.

2. **Cold-start scan + watcher race in `JsonlService` (B1-P1) is a SILENT data-loss bug, not just a UX cold-start gap.** Lines written between `Directory.GetFiles` enumeration and `ParseFileIntoProject`'s `stream.Length` capture get marked as "already read" but never actually read. The visibility-window filter may surface this when 30-day-old sessions fail to re-hydrate after a Claude Code crash. Fix by: starting the watcher BEFORE `DiscoverSessions`, OR by re-reading `stream.Position` after StreamReader exhaustion instead of trusting `stream.Length` snapshot.

3. **B2's "auto-detect wrong org" heuristic is a UX trap if implemented as auto-switching.** Five consecutive zero polls is indistinguishable from a user being on vacation. Implement as a soft-prompt InfoBar (dismissable, single-shot per session), pair with an explicit "Force re-resolve org" button in Settings → Account, and require re-authentication when org changes (WebView2 cookie jar is per-org-context). Document the contract in PROJECT.md Key Decisions before coding.

---

## Sources

- `D:\myProjects\ccInfoWin\CCInfoWindows\CCInfoWindows\Services\JsonlService.cs:223-248,499-540,779-801,828-888` (cold-start scan, watcher debounce, RebuildSessionsList)
- `D:\myProjects\ccInfoWin\CCInfoWindows\CCInfoWindows\Services\UsageHistoryService.cs:25-29,58-79,81-102` (SemaphoreSlim sync+async pattern reference)
- `D:\myProjects\ccInfoWin\CCInfoWindows\CCInfoWindows\ViewModels\MainViewModel.cs:301-303,318-332,371-375,997-1033` (Receive handlers, fire-and-forget pricing load, dispatcher field lifecycle)
- `D:\myProjects\ccInfoWin\CCInfoWindows\CCInfoWindows\Services\Interfaces\IDispatcherTimer.cs` (adapter pattern reference for IDispatcherQueue mirror)
- `C:\Users\DanielMielke\.claude\projects\D--myProjects-ccInfoWin\memory\architecture_weakreferencemessenger_with_transient_vms.md` (Pitfall #1 GC + Pitfall #2 thread-affinity)
- `C:\Users\DanielMielke\.claude\projects\D--myProjects-ccInfoWin\memory\backlog_pricing_never_loaded.md` (B3 silent-failure context)
- `D:\myProjects\ccInfoWin\.planning\PROJECT.md:108-117` (known tech debt deferred to v1.5)
