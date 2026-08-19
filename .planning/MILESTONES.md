# Milestones

## v1.7 Workflow Subagent Visibility (SHIPPED 2026-08-19, tag `v1.7.0`)

**Phases completed:** 5 phases (1-3, 3b, 4, 5), 10 commits, no GSD (plan mode / ultracode)
**Changes:** 22 files, +3,810/-149 lines
**Tests:** 797 → **898** GREEN (809 after phases 1–3, 818 after 3b, 832 after 4, 874 after 5, 879
after the release fixes below, 887 after the keyboard/D-3 pass, 894 after the v1.7 review pass, 898
after the code-clone remediation below)

**This milestone is a Windows-only extension, not parity.** Upstream ccInfo has no workflow
display at all — it predates Claude Code's `Workflow` tool. Every code site that touches it
carries a grepable `Windows-only` marker so a future parity pass does not mistake it for drift.

**Key accomplishments:**

- Workflow subagents were invisible: they live nested under `subagents/workflows/{runId}/`, and
  the scan only looked one level down. A 43-agent run showed nothing at all.
- Each run collapses into **one** row rather than one row per agent — 44 agents would otherwise be
  roughly 1,230 px of bars in a ~600 px window.
- **The row carries no bar and no percentage** (phase 3b, overturning the phase-2 design). Agent
  utilization is a ratio against a *per-agent* ceiling; over a group that ceiling does not exist,
  so maximum, mean and sum alike produce a number with no reference quantity. The row reports the
  two extensive quantities that do add up: `31/31 Agents fertig · 3.3M Tokens`.
- **The 30 s staleness gate had to move from per-agent to per-run.** Applied per agent, the token
  sum showed a median 22 % of the truth and 4.4 % in the frozen last snapshot — the finished agents
  were being filtered out of their own run's total.
- Hover card (phase 5) replaces the phase-4 `ToolTip`: an overlay in the page's visual tree, not a
  `ToolTipService` popup. `ToolTip` could express none of the four requirements — it anchors to its
  target rather than the window, its popup may leave the window (and did, flipping above the top
  edge), and its content cannot scroll. Name, description and phases come from the run script
  (`workflows/scripts/{name}-{runId}.js`), parsed with a bracket-counting, string-aware reader
  because the `meta` block is a JavaScript object literal, not JSON.
- The completed-run JSON was dropped as a source entirely: it is written at run *end*, by which
  time the run has stopped writing and the row is already gone through the staleness gate. That
  branch could never execute against a visible row — dead code wearing the shape of a case
  distinction.

**Release fixes (2026-08-17)** — the four open findings of
`.planning/reviews/2026-08-09_v16-v17-review.md`:

- **B-1** — the version triple and `README.md` still read 1.6.1 while the v1.7 code was complete.
  Tagging that would have produced a permanent, undismissable update banner for every user running
  exactly that build, because `UpdateService` compares the GitHub tag against the assembly version.
- **B-2** — `installer/setup.iss` had flagless `[Tasks]`, which Inno Setup re-checks on every run
  including upgrades, silently restoring autostart and the desktop icon for users who had turned
  them off. `Flags: checkedonce` limits the default to first installs. Verified by actually
  compiling the script — the predecessor `Flags: checked` was equally plausible-looking and equally
  invalid, and went unnoticed for a year because `iscc` was never run.
- **G-2** — nothing repainted a subagent row after the last write, and the last write of a workflow
  run comes from the run's own agents, so a finished run kept its row for hours. The one-minute
  countdown tick now retires rows against the `LastActivity` the service already measured.
  **The review's own prescription ("re-read the context window on the poll tick") was not
  followed, and the roadmap records why:** the poll timer can be switched off entirely ("Manual"),
  and re-reading re-evaluates the service's 30 s gate at an arbitrary wall-clock instant — which
  the review's *own* measurements rule out (26 % of a live 43-agent run had no agent fresh within
  30 s; one agent went 474 s without a write inside a single model call). That would have deleted
  the row of a run still in progress. The retirement window is therefore 10 minutes and independent
  of the display gate, and a test pins it: shrink it below ~8 minutes and the suite goes red.
- **G-3** — `CLAUDE.md` documented `.\dev` as working in "any shell"; in Git Bash the backslash
  escapes the `d` and the command fails. Both forms are now stated.

**Keyboard access and D-3 (2026-08-18)** — the last two open findings, tests 879 → **887**:

- **D-3** — a live run re-read every agent transcript of that run on every poll pass, although the
  finished agents of a live run never change again (12.7 MB / ~3 200 JSON lines measured for the
  largest run). Reads are now memoized per file on `(mtime, length)`. Keyed on the **path**, not on
  the `(path, mtime, length)` triple the review proposed: with the stamp in the key, a file that is
  still being written adds an entry per pass instead of replacing one, so the dictionary would grow
  with a run's write count rather than its agent count. `MaxTokens` is deliberately left out of the
  memo — it comes from the price list, which fills in asynchronously, so caching it would freeze
  whatever that list happened to know at first read.
- **F-4 (keyboard half)** — the hover card hung on pointer events only. `IsTabStop` now rides on the
  same label `TextBlock` the pointer handlers do, which is `Collapsed` on plain rows and therefore
  keeps them out of the tab order without a converter. Focus opens the card, Escape closes it,
  Enter/Space reopen it, Left/Right scroll it. **Measured in the running app** against the mockdata
  fixture, through UIA rather than screenshots (which return blank frames on this machine).
- **D-27, found by that measurement and not by design** — Escape closes the card but leaves focus
  ON the row, so no further `GotFocus` is ever raised and the card was unreachable until the user
  tabbed away and back. Enter/Space reopen it, ahead of the visibility guard; a convention test pins
  that ordering by index comparison.
- **U-6 / U-21 on the same row** — the glyph leaves the automation tree, and moves from
  `TertiaryTextBrush` to `SecondaryTextBrush`: computed, not guessed, at 2.84:1 (dark) / 2.99:1
  (light) against the 3:1 floor of WCAG 1.4.11, versus 5.22:1 / 4.65:1 after. A new test reads the
  palette out of `AppTheme.xaml`, so the brush cannot be quietly reverted.

**Deliberately not built:** screen-reader output (the review's `AutomationProperties.HelpText`
suggestion). The user's call — the app supports no screen reader at all, so one `HelpText` on this
one row would be an island. That also makes U-6 formally moot; it was taken along because it is a
single attribute, not because the finding carries.

**Code-clone remediation (2026-08-19)** — 73 of the 86 confirmed findings of
`.planning/reviews/2026-08-17_code-clone-review.md`, in 8 commits, tests 894 → **898**. Net
**−877 lines** across 72 files (production +1157/−1003, tests +915/−1946). Ten subagents in three
waves, partitioned by **file ownership** rather than by topic — the same rule that made the v1.6
remediation work, and the reason no two agents ever collided.

The review found no high-severity clone: every high claim was downgraded because its failure mode was
cosmetic, compiler-caught or unreachable. Four divergences were *measured* rather than hypothesised,
and those are the whole value of the pass:

- **C3** — the tmp-write / commit / discard sequence was hand-copied into five writer bodies and the
  copies had drifted on a durability invariant: one store wrote without a lock, another without
  tmp+rename. `Services/AtomicJsonFile.cs` owns it once; the write ordering is now structural rather
  than conventional, because the helper returns `bool` and a store assigns its snapshot only on `true`.
- **A2** — the context-window computation existed twice and the copies had diverged on synthetic
  models. **Behaviour change:** a subagent whose last assistant entry is the `<synthetic>` marker now
  resolves back to the previous real model and that model's ceiling, instead of reporting the marker,
  missing pricing and falling back to the 200K default. That is what makes a subagent row agree with
  the session bar above it.
- **F7** — MainView re-declared the Segmented palette inline and its light-theme copy said `#DCDCE0`
  where `AppTheme.xaml` said `#E5E5EA`. Kept `#E5E5EA`: `SessionComboBox` paints from
  `SegmentedBackgroundBrush` and `ShimmerBaseBrush` shares the literal, and nothing documented the
  darker value.
- **E4** — the "labels the localizer cannot reach" pass existed as two drifted copies, so the
  Settings tab strip had **no accessible names at all**. `Helpers/IconLabel.cs` serves both pages;
  verified in-app, all five tabs now report their names through UIA.

Two more behaviour changes worth knowing about: **I7** (`SessionNameSanitizer.Strip` now also drops
C1 controls, U+0080..U+009F — the disagreement the finding reports; bidi codepoints still pass,
because `char.IsControl` covers Cc and not Cf) and **I1** (the ViewModel's private 2-minute rotation
tolerance is gone; it reads `UsageNotificationService.RotationClockSkewTolerance`, so the two layers
can no longer disagree about when a window counts as rotated).

**Three findings were deliberately left undone, each with its reason in the commit that touches the
file:** C5 (sharing `Save`/`SaveAsync` needs either sync-over-async, which deadlocks the dispatcher
`SaveAsync` may be blocking, or splitting the version-before-snapshot ordering across two calls), D1
(the shared seam is more code than it deletes, *and* the finding's fallback of cross-referencing the
twins in the docs would fail `RedirectSeam_HasNoProductionCaller`), and C4-tail
(`ClaudeApiService.LoadCacheAsync` would need a `ReadAsync` built for exactly one caller and would
lose its deliberately narrow catch filter). The 11 findings the review itself classifies as not worth
fixing were not attempted.

**D2 and D5 were pinned, not deduplicated** — which is what those findings actually ask for. The
zone boundaries and the theme colour tables genuinely differ between their two encodings; the missing
guard rail was a cross-theme assertion. `ThresholdBrush` and `AxisLabelBrush` had no assertion at all
and are not declared in `AppTheme.xaml`, so XAML was not cross-checking them either.

**Three test findings changed what the suite verifies rather than how it is written**, and they were
the cheapest items in the review: H1 (`BannerStackPolicyTests` re-implemented the production
banner-priority rule instead of asserting it, so the two could disagree and stay green), G6 (two
pricing suites asserted a hand-copied private production constant, so renaming the cache file would
leave the assertion passing vacuously), and H15 (a tooltip test promised a throwing localizer it never
injected). H6 fixed a live divergence: of the five spellings of the obj/bin filter, the XAML uid
scanner was the only one without an ordinal-ignore-case comparison.

**How the XAML work was verified**, since the suite compiles XAML but never renders it and a wrong
`Style TargetType` fails only at runtime: a pixel diff against the pre-change build.
**SettingsView came out 2 differing pixels of 1.43 M**, and MainView differed only across
y=822–855 — the statistics tab strip, exactly the F7 colour. Method recorded for reuse in
`memory/tooling_xaml_pixel_diff_verification.md`.

**One residual gap, stated rather than papered over:** the model badge that A2's resolution feeds is
covered by a unit test but was never *seen* rendered — the mockdata fixture produces only workflow
rows, which carry no badge. Its downstream render path is the same one the session badge already uses.

---

## v1.6 macOS v1.15.2 Feature Parity (Shipped: 2026-08-09)

**Phases completed:** 6 phases (0-5), 5 commits, no GSD (ultracode / plan mode)
**Changes:** 45 files, +3,399/-705 lines
**Tests:** 345 → 434 GREEN (+89, incl. 3 `AxisLabelFitsInGutter` cases from the U6 fix); baseline made
truly green in phase 0. Then 434 → 797 across the post-UAT remediation waves below.

**Key accomplishments:**

- Context window resolved from live pricing data instead of a hardcoded family map. The old map
  was wrong in both directions: Sonnet at 500K tokens showed 250% and warned permanently, Opus
  4.5 at 300K showed 30% and never warned at all — the app was hiding a real limit. Resolution is
  session evidence (a transcript above 200K proves the window) → `max_input_tokens` → 200K.
- **Roadmap correction:** the above-200k price tier is a VETO, not a gate. The roadmap pseudocode
  said "take the >200K value only if the tier exists", which contradicted its own prose and
  coverage matrix. Verified against real upstream data: the surcharge marks an *opt-in* window
  needing a beta header (Sonnet 4: 1M with tier → effectively 200K), while native 1M models
  (Sonnet 5/4.6, Opus 4.6/4.7/4.8, Opus 5, Fable 5) carry no tier and keep their full window.
- Bundled price table replaced with upstream's, reduced to its 33 anthropic entries — 34 KB
  instead of 234 KB, behaviour-identical because `ParseAndStore` filters on provider anyway.
  `LiteLLMPricingService` now seeds the fallback in its constructor, closing a cold-start race
  that Phase 1 would otherwise have introduced (pricing loads fire-and-forget while
  `RefreshSessionList` already resolves context windows).
- Sonnet context-size setting removed end to end; no migration needed because
  `System.Text.Json` drops the now-unknown property (verified against the live settings.json).
- Chart redesign: Fritsch-Carlson monotone curve (no overshoot above 100%), fill fading to the
  baseline via a two-gradient `FillGeometry`, glow with a white core, an 11px inset on all four
  sides so the glow is never clipped, eased green→yellow ramp, centred axis labels, and one
  shared geometry for fill+line built once per frame. Height 120 → 160; export chart area now
  matches the live canvas exactly rather than within 3.4%.
- Threshold (80/95%) and window-reset notifications — an upstream gap since v1.5.0 that the port
  never had. Implements the end state of v1.15.0/1/2 rather than reproducing three bugs:
  minute-truncated window identity persisted as a string, flags re-armed only by an identity
  change, an `_armedWindowIds` guard so 30-second polls do not restart the countdown forever, and
  `PeakUtilization` instead of last-value so a window reported as 0% during rotation still
  reports. Verified live: both `resets_at` in the first real poll carried sub-second noise
  (`.07056` / `.070581`), exactly the condition that broke upstream.
- Restfixes: `.Distinct()` + ordering on the statistics model row, above-200k *output* price
  applied (a 33% Opus surcharge that was silently dropped), statistics re-aggregated after the
  first successful pricing load, and a steepness filter in `BurnRateCalculator` so a single bogus
  sample cannot fire a false "exhausted in 2 minutes" alarm — `Predict` now has both
  plausibility bounds where upstream has only the upper one.

**Visual UAT (2026-08-06):** run on an unlocked session against the Release build. U1–U11 all pass;
U6 failed on the first attempt (axis labels wrapped — "100%" measures 24.36px into an 18px rect) and
was fixed in `d975646` by widening `ChartRenderer.LeftMargin` 22 → 32. Full record with per-checkpoint
evidence in `.planning/STATE.md`.

**Post-UAT remediation (2026-08-06 … 08-08, branch `fix/repo-review-2026-08-06`):** a full-repo
review of `b16c992` produced 46 findings (2 High, 31 Medium, 13 Low). **All 46 are fixed** across
five waves — 797/797 tests green. Highlights, because several change behaviour the UAT recorded:

- Deduplication was inert against real JSONL: `BuildDeduplicationKey` read a `uniqueHash` field
  Claude Code never writes, so the key was always empty and every assistant turn was counted 2–4×.
  Keyed on `message.id|requestId` now, with the per-line `uuid` as fallback, and a repeated identity
  supersedes the earlier entry rather than being skipped. Every token, statistic and cost figure the
  app displays was roughly 1.9× too high and is now correct.
- `installer/setup.iss` packaged `win-x64\publish\*`, which the mandated `dotnet build -c Release`
  never writes — the copy on disk was a 114-day-old April binary, so a v1.6 tag would have shipped
  v1.0-era code labelled `1.0.0`. Source repointed at the sanctioned output, the orphan tree deleted,
  and the three divergent version strings unified on 1.6.0.
- `AppLog` added: a Release-safe sink at `%LOCALAPPDATA%\CCInfoWindows\app.log` (1 MiB, single roll).
  Roughly 41 `Debug.WriteLine` sites and seven bare catches were routed through it — they were all
  `[Conditional("DEBUG")]` and therefore erased from the build users run, which is what made most of
  the other findings undiagnosable in the field.
- `ShouldWarnAutocompact` compared against the raw maximum minus 20K while `Utilization` divides by
  the maximum minus 33K and clamps, so the warning threshold sat 13,000 tokens *above* the point where
  the bar already reads 100% — it could never fire before the event it exists to pre-announce. Now
  measured against the same baseline. CTX-04's wording and its two pinning tests were updated with it.
- Auth no longer keys off `UnauthorizedAccessException` (a filesystem permission error while writing
  the usage cache was being read as an HTTP 401 and force-logged the user out); `SessionExpiredException`
  is the only 401 signal from the bridge now.
- Localization: `CountdownFormatter` had `de-DE` and its date pattern hardcoded, so English users read
  the weekly reset as "Mi. 06.08." — it now reads `CultureInfo.CurrentUICulture` plus a per-locale
  `WeeklyResetDatePattern` key. Three more German literals and four hardcoded ComboBox labels moved
  into both resw files.
- The Sessions-tab Clear button bound through a `DataContext` that was never set and did nothing;
  `ISessionNameStore.Save`/`SaveAsync` success flags were discarded at all four call sites, so a failed
  rename looked successful and vanished on restart. `LoadSettings` now allow-lists and clamps every
  persisted field — a bad `Language` string used to abort app launch with no recovery path.
- The dashboard bootstrap had no failure surface: one throw left a rendered window with no timers, no
  banner and no log. Timers are created before cache hydration now, and the existing
  `HasApiError`/`ApiErrorMessage` InfoBar is actually used.

**Post-remediation UAT (2026-08-07/08) — the re-look that unblocked the tag.** The original U1–U11
run predated the fixes, so its evidence had to be discarded and re-taken. All nine behaviour changes
the remediation introduced were verified in the running app, **0 regressions**. Record and 21
screenshots in `.planning/reviews/2026-08-07_ui-uat-post-remediation.md` (gitignored).

That pass found five defects, **all pre-existing** rather than caused by the remediation. Four were
fixed and re-verified in-app, then confirmed clean by a manual retest on 2026-08-08 (tests 773 → 797):

- `1ede115` — one malformed LiteLLM pricing entry discarded the whole catalogue, and the failure
  logged on every retry; per-entry parsing plus flood suppression now.
- `0ff3b61` — MainView's icon-only buttons had lost their tooltip and automation name.
- `379879b` — dropdown captions ignored a runtime language switch (`LabeledOption` + `LanguageApplied`).
- `668b2f5` — session rename never persisted: WinUI's default `UpdateSourceTrigger=LostFocus` wrote the
  TextBox back *after* the save command had already read the stale value.

The fifth is deliberately unfixed: the default window size is too small for the footer. Judged a
non-issue — resizing solves it and no data is hidden.

**v1.6.1 (2026-08-09) — installer patch.** `installer/setup.iss` carried `Flags: checked`, which is
not a valid `[Tasks]` flag, so `iscc` aborted before writing anything. It went unnoticed because Inno
Setup was never installed on the build machine: a release step CLAUDE.md mandates had in fact never
run once. Removing the flag is behaviour-neutral — `[Tasks]` entries are pre-selected by default.
v1.6.0 shipped with the broken script, hence the patch tag rather than moving a published tag.

---

## v1.4 macOS v1.11.1 Feature Parity (Shipped: 2026-05-07)

**Phases completed:** 4 phases (20-23), 13 plans (10 base + 3 gap-closure), 51 commits
**Changes:** 64 files, +11,115/-42 lines
**Tests added:** 4 new test classes (`MainViewModelAuthFlowTests`, expanded `UsageHistoryServiceTests`, `SettingsViewModelTimerTests`, `ResourceCoverageTests`); 26+ tests GREEN on modified surface
**Audit:** PASSED (21/23 fully verified, 2 deferred with full unit-test coverage)

**Key accomplishments:**

- Auth flow stability — `_autoReauthAttempted` state machine routes first 401 automatically to LoginView, second 401 to existing InfoBar fallback; `App.MainWindow.Activate()` injected before every frame navigation activates minimized windows reliably; LoginView reload button with 30×30 Slate-900 pill wrapper for cross-surface contrast on cream-white claude.ai login page
- History persistence hardening — `IUsageHistoryService` extended with `SaveHistoryAsync` (poll path, byte-identical to sync) + `PeekLastSnapshot` (termination guard); `SemaphoreSlim(1,1)` prevents concurrent writes; `MainWindow.OnClosing` flushes synchronously via snapshot before process exit; ResetsAt comparison clears Points on 5-hour window reset
- UI polish — `PollUsageCoreAsync` extraction with 250 ms `Task.Delay` floor eliminates spinner flicker; `SessionDisplayItem.TooltipText` recomputes reactively via `SessionTimeoutChangedMessage`; `IDispatcherTimer` adapter on the About-tab makes the timer headless-testable (6 lifecycle tests GREEN)
- Localization gaps — 4 new resw keys (NotSignedIn / NoData / Loading / InactiveSessionTooltip) in DE+EN; `ResourceCoverageTests` validates all 6 L10N-01 keys × 2 locales structurally via `XDocument` — no XAML migration needed (codebase already used `l:Uids.Uid` exclusively)
- Production hotfix pitfall captured — `WeakReferenceMessenger` + `AddTransient<ViewModel>` led to recipient GC and broke logout in production; reverted to direct DI call in `SettingsViewModel.Logout`; architectural memory written for v1.5 review (`architecture_weakreferencemessenger_with_transient_vms.md`)
- Three real bugs caught by UAT, all fixed via gap-closure plans (20-05 reload-button contrast, 21-03 logout history-clearing, 22-04 refresh-button IsEnabled override) — `gap-closure-as-additional-wave` pattern preserved phase-numbering stability
- All 23/23 v1.4 requirements satisfied (AUTH-01..07, HIST-01..05, POLISH-01..08, L10N-01..03)

---

## v1.3 macOS v1.10.0 Feature Parity (Shipped: 2026-04-14)

**Phases completed:** 4 phases (16-19), 7 plans, 20 commits
**Changes:** +1879/-185 lines, 49 new unit tests

**Key accomplishments:**

- Burn rate prediction engine — linear regression over last 15 minutes (min 3 points, min 20% utilization), red banner with flame icon, one-shot toast notification fired exactly once per warning cycle
- BurnRatePrediction model + BurnRateCalculator engine fully TDD'd, theme brush + DE/EN localization for "~Xh YYmin" / "~Xmin" formats
- Chart horizontal gradient — green→yellow→orange→red transitions at 25% opacity over data range, 100% line stroke (2.0px live, 2.5px export), correct gap handling, no bleed into empty chart space
- CanvasLinearGradientBrush wrapped per draw cycle (not cached); CanvasAlphaMode.Premultiplied prevents desaturation in Win2D
- Settings view rewritten with Segmented Control — General/Updates/Account/About tabs at 360px width, colored icon badges, 40px row layout, smooth tab switching without page reload
- IsXxxTabVisible computed bools backed by SegmentedItem.Content (not Icon) for badge rendering; existing InvertedBoolToVisibilityConverter reused
- FileSystemWatcher session detection verified — NotifyFilter and IncludeSubdirectories confirmed via integration tests with IAsyncDisposable cleanup pattern
- All 21/21 v1.3 requirements satisfied (BURN-01..07, CHRT-01..05, SETT-01..08, SESW-01)

---

## v1.2 macOS v1.8.3 Feature Parity (Shipped: 2026-04-13)

**Phases completed:** 4 phases, 6 plans, 14 commits
**Changes:** 23 files, +966/-106 lines

**Key accomplishments:**

- ModelContextLimits rewritten from token-count heuristic to ModelFamily enum — Opus returns 1M, Sonnet uses configurable size, Haiku/Unknown default to 200K; flat 33K buffer and 20K warning threshold *(the 20K is measured against the **effective** max since the 2026-08-06 remediation — against the raw max it could never fire before the bar saturated; see the v1.6 entry)*
- Sonnet context window setting added to Settings view (200K/1M ComboBox) with live refresh via WeakReferenceMessenger — context bars update immediately on setting change
- ISettingsService injected into JsonlService for SonnetContextSize passthrough to GetMaxContextTokens, completing the end-to-end settings→display pipeline
- IsValidProjectDirectory guard filters orphaned sessions (deleted project directories, UNC paths) and subagent context bars sorted alphabetically by AgentId
- Footer tooltip fix: explicit ToolTipService.ToolTip attributes added to all footer buttons — WinUI3Localizer Uid-only injection doesn't create tooltip infrastructure at XAML parse time
- All 17 v1.2 requirements satisfied (CTX-01..06, SET-01..05, SES-01..03, ACC-01..03)

---

## v1.1 UI Polish & UX Improvements (Shipped: 2026-04-01)

**Phases completed:** 3 phases, 6 plans, 10 tasks

**Key accomplishments:**

- MainView section order restructured: Active Session header added, equal 16px padding applied, Context Window moved before 5-Hour Window per macOS reference layout
- Footer relocated into ScrollViewer (scrolls with content), Statistics grid gains Models/Input separator — completing all 6 LAYOUT requirements from Plans 01 and 02
- Pure XAML styling: semi-transparent progress track (#72808080), pill badges (CornerRadius=999), ComboBox with SegmentedBackgroundBrush, and secondary-colored statistics labels with normal weight
- AxisLabelBrush corrected to match SecondaryTextBrush (#8E8E93 dark / #6E6E73 light) — chart axis labels now visually consistent with timer text
- CountdownFormatter extended with >=24h "Xd Yh" branch (TDD), logout button styled red with F3B1 icon, ReLogin button decorated with login icon
- WinUI 3 Storyboard deferred-stop pattern using _stopOnComplete flag — refresh icon always completes its current 360-degree rotation before halting

---

## v1.0 CCInfoWindows MVP (Shipped: 2026-03-17)

**Phases completed:** 8 phases, 21 plans, 2 tasks

**Key accomplishments:**

- (none recorded)

---
