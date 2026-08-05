# Project Research Summary — v1.5 (macOS v1.12.0 Feature Parity + Hardening)

**Project:** CCInfoWindows
**Domain:** Windows desktop port of stefanlange/ccInfo (Claude Code usage monitor)
**Researched:** 2026-05-07
**Confidence:** HIGH (all four research files anchor on in-tree code, in-repo memory artifacts, and verbatim upstream RELEASENOTES; only one MEDIUM area — Decision 3 Tier-2 enforcement mechanism)

## Milestone at a Glance

v1.5 is a **feature-parity + hardening** milestone, not a feature-expansion one. Eleven items across three clusters (A: macOS v1.12.0 parity, B: cold-start/silent-failure bug hardening, C: v1.4 code-review remediation) ship inside the existing C# 13 / .NET 9 / WinUI 3 (WinAppSDK 1.8) / CommunityToolkit.Mvvm 8.4 stack with **zero new top-level NuGet packages**. Two opportunistic patch bumps (`CommunityToolkit.Mvvm` 8.4.0→8.4.2, `Microsoft.WindowsAppSDK` 1.8.260209005→1.8.260416003) are recommended; WinAppSDK 2.0 is explicitly deferred to v1.6+. Recommended decomposition: **5 phases (24–28)** — foundation (C-cluster + IDispatcherQueue) first, then dependent UX work in dependency order (B1 → A2 → A1+B2+B3+M-2 → cleanup).

## Stack Verdict (no new top-level NuGet packages)

| Package | Action | Rationale |
|---------|--------|-----------|
| `CommunityToolkit.Mvvm` | **8.4.0 → 8.4.2** (patch) | Latest stable; bug-fix-only; same-minor; zero risk. Apply in first phase that touches `.csproj`. |
| `Microsoft.WindowsAppSDK` | **1.8.260209005 → 1.8.260416003** (patch) | Latest 1.8.x servicing patch (2026-04-21). 1.8 in Maintenance until 2026-09-09. |
| `Microsoft.WindowsAppSDK 2.0` | **DEFER to v1.6+** | Major jump released 8 days before milestone start. v1.5 already mixes parity + hardening + remediation. Align with future `V2-05: Migration to .NET 10 LTS`. |

**Cluster-by-cluster confirmation — no new dependencies:**

- **A1**: pure XAML + ViewModel + 1 resw key. `FiveHour.ResetsAt` already deserialized.
- **A2**: new internal service `ICustomSessionNameStore` over JSON file at `%LOCALAPPDATA%\CCInfoWindows\session-names.json`, modelled on `SettingsService`. No new NuGet.
- **B1**: existing `JsonlService`, `ISettingsService`, `WinUI3Localizer`. No new NuGet.
- **B2**: `/api/organizations` endpoint already in production at `ClaudeApiService.cs:163` — only `IOrgResolverService.ListAvailableAsync` lift + Settings UI required.
- **B3**: reuses existing `HasApiError` + `InfoBar` pattern. No new NuGet.
- **Cluster C**: pure refactoring + new in-house adapter (`IDispatcherQueue` mirroring `IDispatcherTimer`). No new NuGet.

## Feature Table Stakes

All five Cluster-A/B items classify as **TABLE STAKES**. v1.5 has zero "differentiators" — this is parity + correctness, not competitive features.

| ID | One-line summary | Complexity | Classification |
|----|-----|-----|-----|
| **A1** | Show absolute reset time of 5h-window below countdown ("Mo 1.5. 16:30") | **S** | Table stakes — upstream parity (verbatim RELEASENOTES) |
| **A2** | Rename sessions via pencil button + new "Sessions" Settings tab; names persist in `session-names.json` | **L** | Table stakes — headline upstream feature; auto-derived names are notoriously cryptic |
| **B1** | Fix Cwd-hydration cold-start gap in `JsonlService` + configurable visibility window (7/30/90/unlimited; default 30) | **M** | Table stakes — silent UX failure |
| **B2** | Org-ID picker for multi-account users + zero-utilization heuristic + force re-resolve | **M** | Table stakes — silent breakage of all metrics for multi-account users |
| **B3** | Surface `EnsurePricesLoadedAsync` failure via dedicated `IsPricingError` InfoBar (couples with M-2) | **S** | Table stakes — degraded cost analytics looks like real zero cost |

**Anti-features:** session deletion, cloud sync of names, DPAPI for names, per-session colors/tags, removing `Directory.Exists` validation, full multi-account rebuild, `WeakReferenceMessenger` for rename → refresh.

## Architecture Decisions (the three conflicts, resolved)

### Decision 1 — A2 `ISessionNameStore` hook layer

**Verdict: Hook at the display layer in `MainViewModel.RefreshSessionList` (option b), NOT inside `JsonlService` (option a).**

ARCHITECTURE wins over FEATURES on three concrete grounds:

1. **Testability without filesystem mocking.** `JsonlService` already takes optional `ISettingsService` (cs:106-120 — v1.2 Key Decision PROJECT.md:185 explicitly added that defensiveness to preserve 13+ test constructors). Injecting a *second* optional service doubles that surface. Keeping `JsonlService` storage-free means its tests need only `.jsonl` fixtures, not separate JSON-store mocks.
2. **Single source of truth is preserved at the display layer.** `_sessionNameStore.GetCustomName(s.Id) ?? s.DisplayName` — that one line *is* the single source of truth, applied exactly once per refresh. v1.5 has exactly one consumer chain.
3. **D-13 lesson actually argues *against* option a.** v1.4 D-13 replaced `WeakReferenceMessenger` with **direct DI on a singleton** for logout — pushing cross-cutting state out of broadcast plumbing into a single narrow seam. Option b is the same pattern; option a re-spreads the concern across the service layer (the opposite of D-13's lesson).

**Storage key:** encoded `projectDirName` (= `SessionInfo.Id`), NOT decoded `Cwd`. **Change propagation:** `ISessionNameStore` exposes a .NET `event EventHandler? NameChanged`; `MainViewModel.InitializeAsync` subscribes via `IDispatcherQueue.TryEnqueue(RefreshSessionList)`. **Do NOT introduce a `SessionRenamedMessage`** — re-opens v1.4 transient-VM GC trap.

### Decision 2 — Recommended phase build order

**Verdict: Phase 24 → 25 → 26 → 27 → 28 = ARCHITECTURE's order.**

| Phase | Scope | Why this position |
|-------|-------|-------------------|
| **24** | C-1 + C-2 + `IDispatcherQueue` adapter + Tier-1/2 marshaling-rule enforcement | Establishes project-wide `IRecipient<>` rule; phases 25–27 use `IDispatcherQueue` from day one. Landing later forces rebase on every new `IRecipient<>`. |
| **25** | B1 — `JsonlService` Cwd hydration + visibility-window ComboBox + `SessionVisibilityChangedMessage` | Must precede A2: A2's display layer reads the `SessionInfo` collection that B1 makes reliable. |
| **26** | A2 — Session renaming (`ISessionNameStore` + Sessions Settings tab + MainView pencil + 5th Segmented tab + purple badge) | Builds on B1's stable session list. Uses `IDispatcherQueue` for `NameChanged` event marshaling. |
| **27** | A1 + B2 + B3 + M-2 | Mid-risk feature trio with non-overlapping file surfaces. M-2 *must* couple with B3 (shared `LastFetchRelativeTime` surface). |
| **28** | M-1 + M-3 + Nits + final UAT pass | Pure cleanup. Lowest risk; ships last to keep test surface stable. |

**Why this beats FEATURES' "A1 first" ordering:** every phase 25/26/27 adds a new `IRecipient<>`. Without C-2's `IDispatcherQueue` rule landing first, those additions either skip marshaling (replicate v1.4 production bug) or need rewrite once C-2 lands. Phase 24 first absorbs rebase pain into the foundation phase. **C-1 and C-2 stay paired** — same `Receive(AuthStateChangedMessage)` body, single edit, single test (PITFALLS C1-P1).

### Decision 3 — `IDispatcherQueue` full adapter scope in v1.5

**Verdict: YES — full adapter (interface + `WinuiDispatcherQueueAdapter` + `FakeDispatcherQueue` + convention test) lands in Phase 24.**

1. **v1.4 precedent unambiguous.** v1.4 shipped `IDispatcherTimer` adapter alongside the About-tab fix; six lifecycle tests went GREEN immediately. Codebase carries the template ready to mirror.
2. **C-2 is CRITICAL; a critical fix without a test gate is paper.** Asserting "C-2 marshals to UI thread" via xUnit requires verifying `TryEnqueue` was invoked with the right delegate — impossible against a concrete `DispatcherQueue` field without spinning a real WinUI dispatcher.
3. **Surface is tiny.** `IDispatcherQueue { bool TryEnqueue(Action); bool HasThreadAccess; }` covers every site. Adapter ~15 lines; fake ~5. Convention test locks the rule across the codebase. Total Phase 24 cost: well under a day.

**Open detail (Phase-24 30-min spike):** convention-test mechanism is MEDIUM confidence — pure reflection cannot inspect a method body's first statement; practical fallback is `[ThreadSafeReceive]` / `[RequiresMarshal]` attribute pair.

## Watch Out For (Top 3 Pitfalls)

1. **Silent off-thread `IRecipient<>.Receive` mutation (G-1, C2-P1).** Every `Receive` body touching `[ObservableProperty]`, navigation, or XAML must wrap in `IDispatcherQueue.TryEnqueue` — **including** the else-branch when already on the UI thread. `WeakReferenceMessenger.Send` runs receivers synchronously on sender's thread (memory:55); recursive `Receive` chains on UI thread execute inside parent's stack frame, producing mid-update inconsistent state. **Always-TryEnqueue, no `if (!HasThreadAccess)` shortcut.** Anchored at `MainViewModel.cs:997-1026`. Required for every new handler in A2/B1/B3.

2. **Cold-start scan vs. file-watcher race in `JsonlService` is silent data loss (B1-P1, JsonlService.cs:223-248,499-540,828-888).** Lines written between `Directory.GetFiles` (line 509) and `ParseFileIntoProject`'s `stream.Length` capture (line 444) get marked "already read" but never consumed. `_filePositions[filePath]` stores `endPos` ahead of unread bytes; the subsequent FileSystemWatcher Changed event reads only bytes after that position — **lost lines, never counted**. The visibility-window filter does NOT fix this; it can mask it. Phase-25 fix: start watcher BEFORE `DiscoverSessions`, OR replace `stream.Length` with `stream.Position` after final `ReadLine`.

3. **B2 "auto-detect wrong org" heuristic is a UX trap if implemented as auto-switching (B2-P1, B2-P2).** Five consecutive zero-utilization polls is indistinguishable from "user on vacation". Implement explicit-button path FIRST (deterministic — `IClaudeApiService.ListOrganizationsAsync` + force re-resolve in Settings → Account); add heuristic as **dismissable InfoBar soft-prompt** LAST. **Crucial contract:** switching orgs requires re-authentication — WebView2 cookie jar at `%LOCALAPPDATA%\CCInfoWindows\WebView2` is per-org-context. Picker must trigger `MainViewModel.Logout` sequence (cs:936). Document in PROJECT.md Key Decisions before coding.

## Cross-Cutting Conventions (Cluster-C Hardening Rules)

These graduate from informal practice to documented `CLAUDE.md` conventions in Phase 24:

- **G-1 — Marshaling rule for `IRecipient<>`:** every `Receive(T)` body that mutates `[ObservableProperty]`, calls `INavigationService`, or touches XAML controls MUST wrap in `IDispatcherQueue.TryEnqueue(() => HandleCore(...))`. Exception only with proof every sender is UI-thread (documented via `[ThreadSafeReceive]` or inline comment). Enforced by `MessengerThreadingConventionTests` (Tier-2 reflection-based test in Phase 24).
- **G-2 — JSON-on-disk store pattern:** every new persistence service writing to `%LOCALAPPDATA%\CCInfoWindows\*.json` follows `IUsageHistoryService` shape — `private readonly SemaphoreSlim _writeLock = new(1, 1);`, sync + async write methods, `_lastSavedSnapshot` cache, atomic-rename via `tmp + File.Move`. **Never use `lock` keyword across `await`**. First v1.5 consumer: `SessionNameStore` (A2).
- **G-3 — `[ObservableProperty]` defaults:** prefer `= string.Empty;`, `= "--";`, or `= ParseHexBrush(...)` field initializers over `null!`. M-3 is the precedent fix for `_contextModelBadgeColor`.
- **Cross-VM communication priority:** direct DI > singleton-service .NET event > `WeakReferenceMessenger`. Use `WeakReferenceMessenger` only for true broadcast. Exactly-once flows (logout, save-on-close, A2 rename → refresh) use direct DI or singleton events.

## Open Questions Surfaced (deferred to `/gsd-discuss-phase`)

- **A2 Settings-tab placement (Phase 26):** insert as 5th segment between **Account and About** (purple badge); width validation at 360px (badges 30×30 → fall back to 28×28 only if clipping).
- **A2 inline-edit vs. ContentDialog (Phase 26):** **ContentDialog from MainView** (modal avoids 360px-row layout breakage); **inline edit per row in Settings Sessions tab** (no width constraint).
- **B1 visibility-window default for upgrading users (Phase 25):** **30 days for new installs; existing installs migrate to 30 with one-time toast** "Sessions older than 30 days are now hidden".
- **B2 `/api/organizations` payload verification (Phase 27):** endpoint already in production (`ClaudeApiService.cs:163`) — risk LOW. Spike: confirm response includes user-displayable org name (not just `uuid`). **Heuristic threshold:** `OrgMismatchPollThreshold = 5` as tuneable code constant.
- **B3 banner stack-order policy (Phase 27):** cap visible banners at 2; suppress `IsPricingError` rendering when `IsSessionExpired == true`. Document as Key Decision.

## Localization Delta

**~30 new resw keys total** across both `de-DE/Resources.resw` and `en-US/Resources.resw`:

| Cluster | Count | Notes |
|---------|-------|-------|
| A1 | 1 | `FiveHourNextWindowStart.Text` (+ optional today/tomorrow/future format keys if A1-P1 format-switching adopted) |
| A2 | 11 | Pencil tooltip + ContentDialog labels + Sessions tab header + list headers |
| B1 | 6 | Visibility-window header + 4 ComboBox options + migration toast |
| B2 | 5 | Settings labels + InfoBar warning text |
| B3 + M-2 | 9 | Pricing banner (3) + LastFetch* relative-time strings (5 — **M-2 unmasks pre-existing EN-only hardcoded strings**, correctness work, not new feature L10N) + cost-unavailable placeholder (1) |

**`ResourceCoverageTests` extension required** — add all new keys to structural validation.

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | **HIGH** | NuGet versions verified; endpoint verified in-code (cs:163); macOS pattern verified via `gh api` |
| Features | **HIGH** | Upstream RELEASENOTES.md verbatim quote verified via WebFetch; backlog memory user-authored |
| Architecture | **HIGH** | All decisions cite specific file:line evidence; `IDispatcherTimer` precedent shipped in v1.4 |
| Pitfalls | **HIGH** | All pitfalls anchor on in-tree lines or v1.4 hotfix memory; one MEDIUM caveat on Tier-2 enforcement mechanism (Phase-24 spike) |

**Overall confidence: HIGH.**

### Gaps to Address During Planning

- **Phase 24 spike (30 min):** confirm convention-test mechanism — likely fallback is `[ThreadSafeReceive]` / `[RequiresMarshal]` attribute pair.
- **Phase 27 spike (low risk):** confirm `/api/organizations` payload contains user-displayable org name (not just uuid).
- **Phase 25 acceptance criterion:** B1 Cwd race fix needs explicit data-loss regression test, not just visibility-filter UX coverage.
- **Phase 26 acceptance criterion:** Segmented Control 5-tab width verification at 360px.

## Sources

### Primary research files (`D:\myProjects\ccInfoWin\.planning\research\`)
- `STACK.md` — NuGet/SDK verdicts, no-new-dependencies confirmation, `IDispatcherQueue` design
- `FEATURES.md` — verbatim upstream quotes, table-stakes classification, L10N delta
- `ARCHITECTURE.md` — five architectural decisions with cited file:line, build-order analysis
- `PITFALLS.md` — 11 cluster-specific + 3 cross-cutting rules, all anchored to in-tree code

### Most-cited external sources
- **macOS reference (verified via `gh api`):** [`stefanlange/ccInfo` `CustomSessionNameStore.swift`](https://github.com/stefanlange/ccInfo/blob/main/ccInfo/ccInfo/Services/CustomSessionNameStore.swift), `SessionRenameModel.swift`, RELEASENOTES v1.12.0
- **NuGet & SDK channels:** [CommunityToolkit.Mvvm](https://www.nuget.org/packages/CommunityToolkit.Mvvm), [Microsoft.WindowsAppSDK](https://www.nuget.org/packages/Microsoft.WindowsAppSDK), [Windows App SDK release channels (Microsoft Learn)](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-channels)
- **Anthropic Admin API non-applicability note:** [`platform.claude.com` Admin API](https://platform.claude.com/docs/en/api/admin/organizations) requires `sk-ant-admin-…` keys (different surface). Web-app `/api/organizations` is the only realistic source — already in production at `ClaudeApiService.cs:163`.

### In-house architectural memory
- `architecture_weakreferencemessenger_with_transient_vms.md` — drives Decision 1b and G-1 rule
- `backlog_next_window_start_label.md` (A1), `backlog_session_dropdown_recent_sessions.md` (B1), `backlog_org_id_picker.md` (B2), `backlog_pricing_never_loaded.md` (B3)
- `cloudflare-fix.md` — WebView2 bridge, Credential Manager keys

### Anchor reference paths
- `D:\myProjects\ccInfoWin\.planning\PROJECT.md` (lines 49-77 for v1.5 scope)
- `D:\myProjects\ccInfoWin\CCInfoWindows\CCInfoWindows\Services\JsonlService.cs:223-248,499-540,779-801,828-888`
- `D:\myProjects\ccInfoWin\CCInfoWindows\CCInfoWindows\Services\UsageHistoryService.cs:25-29,58-79,81-102` (G-2 reference)
- `D:\myProjects\ccInfoWin\CCInfoWindows\CCInfoWindows\ViewModels\MainViewModel.cs:301-303,318-332,371-375,997-1033`
- `D:\myProjects\ccInfoWin\CCInfoWindows\CCInfoWindows\Services\Interfaces\IDispatcherTimer.cs` (`IDispatcherQueue` template)

---
*Research completed: 2026-05-07*
*Ready for roadmap: yes — proceed to Phase 24–28 sequence per Decision 2*
