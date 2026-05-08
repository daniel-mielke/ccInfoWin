# Phase 27: Next-Window Label, Org-ID Picker, Pricing Surfacing & L10N - Context

**Gathered:** 2026-05-08
**Status:** Ready for planning
**Mode:** Smart-discuss auto-resolved (autonomous run; defaults from REQUIREMENTS, ROADMAP, research/SUMMARY.md)

<domain>
## Phase Boundary

Phase 27 ships FOUR independent features in a combined wave because their file surfaces don't overlap:

1. **NEXTWIN (3 reqs):** absolute next 5h-window start time label below the existing countdown in MainView. Display switches DE/EN format via CurrentUICulture.
2. **ORGID (5 reqs):** "Re-detect organization" button on Settings Account tab + ContentDialog for org selection + soft-prompt InfoBar after 5 consecutive zero-utilization polls + in-memory dismissal.
3. **PRICING (3 reqs):** IsPricingError InfoBar in MainView surfaces silent pricing-service failures + banner-stack policy (max 2 visible, IsSessionExpired suppresses IsPricingError).
4. **L10N (3 reqs):** SettingsViewModel.LastFetchRelativeTime reads from 5 new resw keys + extend ResourceCoverageTests for all ~30 v1.5 keys.

**Strict scope:** No new features beyond these 14 IDs. No org-mismatch threshold tuning at runtime (constant `OrgMismatchPollThreshold = 5`). No pricing service refactor — only surfacing existing failure path.

</domain>

<decisions>
## Implementation Decisions

### NEXTWIN cluster (NEXTWIN-01..03)

- **D-NW-01: Label position.** Below the existing 5h-window countdown text. Same MainView Grid row stack — adds a TextBlock element after the countdown TextBlock.
- **D-NW-02: Visibility binding.** When `UsageResponse.FiveHour.ResetsAt` is null OR `IsSessionExpired==true`, the label has `Visibility=Collapsed` (NOT showing "—"). Use a new `IsNextWindowLabelVisible` ObservableProperty driven by `OnFiveHourResetsAtChanged` partial method.
- **D-NW-03: Format string.** Format via `CultureInfo.CurrentUICulture` — DE: `"Mo 1.5. 16:30"` (`ddd d.M. HH:mm`), EN: `"Wed 14:30"` (`ddd HH:mm`). Two resw keys: `MainView.NextWindow.LabelDe` and `.LabelEn` providing the format strings; OR a single resw key with `string.Format` placeholders. Plan Phase decides — recommendation: 2 keys, format selected at bind time via Localizer.Get().
- **D-NW-04: ObservableProperty additions.** `MainViewModel.FiveHourNextWindowText` (string) and `IsFiveHourNextWindowVisible` (bool). Computed in `OnFiveHourResetsAtChanged` partial method.

### ORGID cluster (ORGID-01..05)

- **D-OG-01: ListAvailableOrganizationsAsync API extraction.** The existing private `TryMigrateOrgIdAsync` in `ClaudeApiService.cs:163` already calls `/api/organizations`. Extract a public `Task<IReadOnlyList<OrganizationInfo>> ListAvailableOrganizationsAsync(CancellationToken ct)` method that returns the parsed orgs (name + uuid). Add `OrganizationInfo` record to Models/.
- **D-OG-02: Re-detect button placement.** Settings Account tab — below or next to the existing "Logout" button. Plan Phase decides exact layout.
- **D-OG-03: ContentDialog flow.** PrimaryButton "Switch" / SecondaryButton "Cancel". ListView of orgs (radio-button style, single-select) with name + uuid columns. On Switch: persist new org-id to `claude-org` Credential Manager key, then call `MainViewModel.Logout()` (which clears WebView2 + shows LoginView).
- **D-OG-04: Soft-prompt mechanism.** New `MainViewModel._zeroUtilizationPollCount` int. Increment in `PollUsageCoreAsync` when `utilization==0 && hasActiveSession`. Reset to 0 when utilization > 0. When count >= `OrgMismatchPollThreshold = 5` AND `!_orgMismatchSuppressed`, set `IsOrgMismatchPromptVisible=true`.
- **D-OG-05: In-memory dismissal.** `_orgMismatchSuppressed` is a private field (NOT persisted). Reset to false on app start. Dismissal flow: user checks "Don't show again this session" + closes InfoBar → `_orgMismatchSuppressed = true`. NOT persisted to AppSettings.
- **D-OG-06: Localized strings.** ~6 resw key pairs: `Settings.Account.RedetectButton`, `Dialog.OrgPicker.{Title, SwitchButton, CancelButton}`, `MainView.OrgMismatchInfoBar.{Title, Message, ResolveButton, SuppressCheckbox}`.

### PRICING cluster (PRICING-01..03)

- **D-PR-01: IsPricingError ObservableProperty.** New `MainViewModel.IsPricingError` (bool). Set true when `_pricingService.EnsurePricesLoadedAsync()` throws (catch block exists at line 371-375 and currently swallows — Phase 27 surfaces it).
- **D-PR-02: InfoBar surface.** MainView. Severity=Warning. IsClosable=false (cleared automatically on subsequent success). Message: localized "Pricing data unavailable — cost figures may be inaccurate" / "Preisdaten nicht verfügbar — Kostendaten können ungenau sein."
- **D-PR-03: Auto-clear on retry.** When pricing succeeds on a subsequent poll (manual refresh OR auto-poll), `IsPricingError = false`. The catch block at line 371-375 surfaces; the success path clears.
- **D-PR-04: Banner-stack policy.** New `IsPricingErrorVisible` computed property:
  ```csharp
  public bool IsPricingErrorVisible => IsPricingError && !IsSessionExpired;
  ```
  Maximum 2 banners visible simultaneously rule: enforced by suppression — `IsPricingError` is suppressed when `IsSessionExpired == true` (auth banner takes priority). Document as Key Decision in PROJECT.md after Phase 27 ships.
- **D-PR-05: Banner-stack policy test.** xUnit test verifies that when both `IsPricingError=true` AND `IsSessionExpired=true`, the visible-banner count is 1 (auth wins). When only `IsPricingError=true`, count is 1. When only `IsSessionExpired=true`, count is 1.

### L10N cluster (L10N-01..03)

- **D-L10-01: 5 new LastFetchRelative keys.** `LastFetchRelative.JustNow`, `.MinutesAgo`, `.HoursAgo`, `.DaysAgo`, `.Never`. Format placeholders: `.MinutesAgo` = "{0} minutes ago" / "vor {0} Minuten" (use `string.Format` after `Localizer.Get`). `.JustNow` and `.Never` are static strings.
- **D-L10-02: SettingsViewModel.LastFetchRelativeTime refactor.** Currently returns hardcoded English strings (memory note WR-01-style). Replace with switch-expression on time-delta categories that calls `Localizer.GetLocalizedString(...)` per category. Surface via PropertyChanged when LastFetchTime updates.
- **D-L10-03: ResourceCoverageTests extension.** xUnit test already validates DE+EN parity structurally. Phase 27 adds: enumerate ALL ~30 v1.5 new keys (DROPDOWN-X visibility window keys from Phase 25 + RENAME tab/dialog keys from Phase 26 + NEXTWIN/ORGID/PRICING/L10N keys from Phase 27) and assert presence in both locales. Plan Phase decides whether to add an explicit list or use a `Settings.*|Dialog.*|MainView.*|Toast.*|LastFetchRelative.*` glob pattern.

### Carrying Forward (locked from prior phases)

- **L-01:** `IDispatcherQueue` constructor-injected (Phase 24). Phase 27 reuses for any new IRecipient handlers (none planned — InfoBar property-driven UI).
- **L-02:** G-1 — any new `IRecipient<T>` handlers MUST wrap in `_dispatcherQueue.TryEnqueue`. Convention test catches violations.
- **L-03:** G-2 — Phase 27 does NOT introduce new JSON stores. AppSettings persistence path unchanged.
- **L-04:** Phase 26 SegmentedControl extended to 5 tabs; Phase 27 does NOT add a 6th tab. ORGID button lives in existing Account tab (4th).
- **L-05:** WeakReferenceMessenger NOT used for cross-VM propagation in Phase 27 — InfoBar visibility is property-driven from MainViewModel directly.

### Out of Scope (explicit)

- **O-01:** Pricing service architectural refactor (Phase 27 only surfaces failures, not fix).
- **O-02:** OrgMismatchPollThreshold runtime tunability (constant only).
- **O-03:** Pricing data caching strategy changes (out of scope — only surface failures).
- **O-04:** Bidi character handling in any new TextBox inputs.
- **O-05:** Persistent dismissal of org-mismatch prompt across app restarts (in-memory only per ORGID-04).
- **O-06:** Org switching without re-authentication (cookie jar is per-org per ORGID-02 — no shortcut).
- **O-07:** CLEANUP wave (Phase 28).

### Claude's Discretion

- **CD-01: NEXTWIN format-string strategy.** Two resw keys (DE-format + EN-format) vs one key with string.Format placeholders. Recommendation: two keys + Localizer.Get(culture-matched) — simpler reading site.
- **CD-02: ORGID dialog UX detail.** ListView vs RadioButton group for single-select org. Recommendation: ListView with single-selection (more idiomatic for variable-length lists).
- **CD-03: Banner-stack policy enforcement.** Pure XAML Visibility binding vs computed property in ViewModel. Recommendation: computed property `IsPricingErrorVisible` — testable in xUnit without UI.
- **CD-04: Pricing-error retry trigger.** Auto-poll alone vs manual refresh button retries pricing. Recommendation: both — when poll succeeds OR manual refresh succeeds, clear IsPricingError.
- **CD-05: ResourceCoverageTests scope mechanism.** Explicit key list vs glob pattern. Recommendation: glob pattern matching common prefixes (`Settings.*`, `Dialog.*`, `MainView.*`, `Toast.*`, `LastFetchRelative.*`) — automatically picks up new keys added by future phases.
- **CD-06: Plan decomposition.** Likely 4 plans (one per feature) OR 2-3 plans bundling smaller features. Plan Phase decides — recommendation: 4 plans (each feature is independent enough to test in isolation), all autonomous=true except ORGID (which has Logout flow side-effect).

</decisions>

<canonical_refs>
## Canonical References

### Phase 27 deliverable scope
- `.planning/REQUIREMENTS.md` §"Cluster A1 NEXTWIN", §"B2 ORGID", §"B3 PRICING", §"M-2 L10N".
- `.planning/ROADMAP.md` §"Phase 27" — 5 success criteria.

### Architectural research
- `.planning/research/PITFALLS.md` §"Cluster B2" — org-id mismatch + cookie-jar partitioning.
- `.planning/research/SUMMARY.md` Decision 6 (banner stack policy).
- Memory note `backlog_org_id_picker.md` — root cause + UX flow.
- Memory note `backlog_pricing_never_loaded.md` — pricing-service silent-failure.
- Memory note `backlog_next_window_start_label.md` — macOS v1.12.0 reference.

### In-tree code anchors
- `CCInfoWindows/CCInfoWindows/Services/ClaudeApiService.cs:163` — existing `/api/organizations` endpoint usage in `TryMigrateOrgIdAsync`. Extract `ListAvailableOrganizationsAsync` from this private method.
- `CCInfoWindows/CCInfoWindows/Services/ClaudeApiService.cs` — also add `OrganizationInfo` record for the public API contract.
- `CCInfoWindows/CCInfoWindows/Services/Interfaces/IClaudeApiService.cs` — add public method signature.
- `CCInfoWindows/CCInfoWindows/Models/UsageResponse.cs` — `FiveHour.ResetsAt` is the source of NEXTWIN absolute time.
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs` — add NEXTWIN ObservableProperty + ORGID prompt state + IsPricingError ObservableProperty.
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs:371-375` — pricing fire-and-forget catch block (PRICING-01 surfacing site).
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs:LastFetchTime field` — drives L10N relative-time computation (look for any existing wiring).
- `CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs:LastFetchRelativeTime` — L10N-01 refactor target.
- `CCInfoWindows/CCInfoWindows/Views/MainView.xaml` — add NEXTWIN TextBlock + ORGID InfoBar + IsPricingError InfoBar. AvailableSpace check: ~3 InfoBars (auth + pricing + org-mismatch + the existing IsSessionVisibilityMigration from Phase 25 + IsApiError) — banner stack policy enforcement matters here.
- `CCInfoWindows/CCInfoWindows/Views/SettingsView.xaml` — Account tab — add Re-detect button + ContentDialog.
- `CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw` and `en-US/Resources.resw` — ~16 new key pairs across all 4 features.

### Localization targets summary
- `MainView.NextWindow.LabelDe`, `.LabelEn` (or single key + format)
- `Settings.Account.RedetectButton`
- `Dialog.OrgPicker.{Title, SwitchButton, CancelButton, NameColumn, UuidColumn}`
- `MainView.OrgMismatchInfoBar.{Title, Message, ResolveButton, SuppressCheckbox}`
- `MainView.PricingErrorInfoBar.{Title, Message}`
- `LastFetchRelative.{JustNow, MinutesAgo, HoursAgo, DaysAgo, Never}`

### Test targets
- New `CCInfoWindows.Tests/ViewModels/BannerStackPolicyTests.cs` (D-PR-05 verification)
- Extend `CCInfoWindows.Tests/Convention/ResourceCoverageTests.cs` for L10N-03
- Optional: `OrgMismatchSoftPromptTests.cs` (poll-counter increment + threshold trigger + suppression flag)
- Optional: `LastFetchRelativeTimeTests.cs` (L10N-01 verification — DE/EN switch via culture)

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- **`InfoBar` pattern from Phase 25:** Migration toast InfoBar (`MigrationToastInfoBar`) is the template. Copy the IsOpen-binding pattern.
- **`ContentDialog` pattern from Phase 26:** RenameSession dialog is the template for OrgPicker dialog.
- **`IDispatcherQueue` (Phase 24):** for any property-changed handlers that may run off-thread.
- **WinUI3Localizer `Localizer.Get().GetLocalizedString(key)` pattern:** already used widely; L10N-01 just calls it differently (per category instead of hardcoded English).
- **`MainViewModel.PollUsageCoreAsync` lines 428-458:** existing `HasApiError` / `ApiErrorMessage` plumbing — reference for IsPricingError pattern.

### Established Patterns
- **`[ObservableProperty]` for InfoBar state:** mirrors IsSessionVisibilityMigrationToastVisible from Phase 25.
- **`[RelayCommand]` for view actions:** OpenOrgPicker, ResolveOrgMismatch, SuppressOrgMismatchPrompt.
- **Existing `IsSessionExpired` auth banner:** highest-priority banner; PRICING/ORGID must respect.

### Integration Points
- **`ClaudeApiService`:** extract `ListAvailableOrganizationsAsync` from `TryMigrateOrgIdAsync` private logic. Add interface method.
- **`MainViewModel`:** add 4-5 new ObservableProperty fields (FiveHourNextWindowText, IsFiveHourNextWindowVisible, IsPricingError, IsOrgMismatchPromptVisible, _zeroUtilizationPollCount internal field). Add 1-2 RelayCommands (ResolveOrgMismatchCommand, SuppressOrgMismatchPromptCommand). Wire IsPricingError set/clear in PollUsageCoreAsync catch + success paths. Wire `_zeroUtilizationPollCount` increment in PollUsageCoreAsync.
- **`MainView.xaml`:** add 1 TextBlock (NEXTWIN) + 2 InfoBars (PRICING + ORGID) + ContentDialog hookup if user clicks ResolveOrgMismatch button.
- **`SettingsView.xaml`:** add 1 Button (Re-detect) + ContentDialog (OrgPicker).
- **`SettingsViewModel`:** rewrite `LastFetchRelativeTime` getter to use Localizer.

</code_context>

<specifics>
## Specific Ideas

- **NEXTWIN format string usage:**
  ```csharp
  // In OnFiveHourResetsAtChanged partial:
  if (FiveHourResetsAt is null || IsSessionExpired)
  {
      IsFiveHourNextWindowVisible = false;
      return;
  }
  var formatKey = CultureInfo.CurrentUICulture.Name.StartsWith("de", OrdinalIgnoreCase)
      ? "MainView.NextWindow.LabelDe"
      : "MainView.NextWindow.LabelEn";
  var format = Localizer.Get().GetLocalizedString(formatKey); // e.g. "ddd d.M. HH:mm"
  FiveHourNextWindowText = FiveHourResetsAt.Value.LocalDateTime.ToString(format, CultureInfo.CurrentUICulture);
  IsFiveHourNextWindowVisible = true;
  ```
- **ORGID poll-counter wiring:**
  ```csharp
  // In PollUsageCoreAsync after successful fetch:
  if (response.Utilization == 0 && _hasActiveSession)
  {
      _zeroUtilizationPollCount++;
      if (_zeroUtilizationPollCount >= OrgMismatchPollThreshold && !_orgMismatchSuppressed)
          IsOrgMismatchPromptVisible = true;
  }
  else
  {
      _zeroUtilizationPollCount = 0;
      IsOrgMismatchPromptVisible = false;
  }
  ```
- **PRICING surfacing site (around line 371-375):**
  ```csharp
  try { await _pricingService.EnsurePricesLoadedAsync(); IsPricingError = false; }
  catch (Exception ex) { Debug.WriteLine($"Pricing load failed: {ex.Message}"); IsPricingError = true; }
  ```
- **L10N relative-time refactor sketch:**
  ```csharp
  public string LastFetchRelativeTime
  {
      get
      {
          if (!_lastFetchTime.HasValue) return Localizer.Get().GetLocalizedString("LastFetchRelative.Never");
          var delta = DateTimeOffset.UtcNow - _lastFetchTime.Value;
          if (delta.TotalSeconds < 30) return Localizer.Get().GetLocalizedString("LastFetchRelative.JustNow");
          if (delta.TotalMinutes < 60)
              return string.Format(Localizer.Get().GetLocalizedString("LastFetchRelative.MinutesAgo"), (int)delta.TotalMinutes);
          if (delta.TotalHours < 24)
              return string.Format(Localizer.Get().GetLocalizedString("LastFetchRelative.HoursAgo"), (int)delta.TotalHours);
          return string.Format(Localizer.Get().GetLocalizedString("LastFetchRelative.DaysAgo"), (int)delta.TotalDays);
      }
  }
  ```

</specifics>

<deferred>
## Deferred Ideas

- **Pricing service refactor:** out of scope (only surfacing).
- **OrgMismatchPollThreshold runtime tuning:** out of scope.
- **Persistent org-mismatch dismissal:** in-memory only per ORGID-04.
- **Bidi handling in any new TextBox:** out of scope.
- **CLEANUP wave (G-3 docs, M-1/M-3 fixes, Nits):** Phase 28.
- **Final UAT pass:** Phase 28.

</deferred>

---

*Phase: 27-NEXTWIN-ORGID-PRICING-L10N*
*Context gathered: 2026-05-08 (smart-discuss auto-resolved)*
