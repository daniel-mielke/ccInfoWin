# Milestones

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
