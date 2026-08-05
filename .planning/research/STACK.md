# Technology Stack — v1.5 (macOS v1.12.0 Feature Parity + Hardening)

**Project:** CCInfoWindows
**Milestone:** v1.5 (subsequent milestone)
**Researched:** 2026-05-07
**Headline finding:** **No new top-level NuGet dependencies required.** Two opportunistic patch-version bumps available; one major-version bump (`Microsoft.WindowsAppSDK 1.8 → 2.0`) is **not recommended** for v1.5.

---

## Existing Stack — DO NOT change

The v1.4-validated stack is fixed for v1.5. Listed here for traceability — every cluster fits inside it.

| Component | Version | Notes |
|-----------|---------|-------|
| C# | 13 | `<LangVersion>13.0</LangVersion>` |
| .NET | 9.0 | `net9.0-windows10.0.19041.0` |
| `Microsoft.WindowsAppSDK` | `1.8.260209005` | WinUI 3 host |
| `CommunityToolkit.Mvvm` | `8.4.0` | `[ObservableProperty]`, `[RelayCommand]`, `WeakReferenceMessenger` |
| `CommunityToolkit.WinUI.Controls.Segmented` | `8.2.251219` | Settings tabs |
| `Microsoft.Graphics.Win2D` | `1.3.2` | Chart rendering |
| `Microsoft.Extensions.DependencyInjection` | `9.0.0` | DI container |
| `AdysTech.CredentialManager` | `3.1.0` | DPAPI token storage (also used for `claude-org` blob) |
| `WinUI3Localizer` | `2.3.0` | `l:Uids.Uid` runtime DE/EN switch |
| `Microsoft.Windows.SDK.BuildTools` | `10.0.26100.4654` | Build-only |
| `xUnit` + `FluentAssertions` | (test project) | F.I.R.S.T. test surface |
| Inno Setup | (external) | Installer |

**Existing in-house abstractions reused by v1.5:**
- `IDispatcherTimer` + `WinuiDispatcherTimerAdapter` — added in v1.4 for headless About-tab timer testing. The adapter pattern is the **template for C-2 below**.
- `WebViewBridge` — Cloudflare-bypass JS-fetch → `postMessage` → `WebMessageReceived` pattern. Already calls `/api/organizations` (see `ClaudeApiService.cs:163`).
- `ISettingsService` (`SettingsService.cs`) — loads/saves `%LOCALAPPDATA%\CCInfoWindows\settings.json` via `System.Text.Json`. Template for any new JSON-on-disk store.
- `ICredentialService` — DPAPI wrapper; stores both `claude-session` and `claude-org` targets.

---

## v1.5 New / Updated Dependencies

### NEW packages

**None.** Every v1.5 capability (Clusters A, B, C) is implementable inside the existing stack. This is a deliberate finding — the synthesizer can surface it as a positive.

### Updated packages — recommended

| Package | Current | Recommended | Type | Rationale |
|---------|---------|-------------|------|-----------|
| `CommunityToolkit.Mvvm` | `8.4.0` | `8.4.2` | Patch | Latest stable on NuGet (published 2026-03-25). Bug-fix-only on the same minor line; zero source-level risk. **Do this in the first phase touching the .csproj.** |
| `Microsoft.WindowsAppSDK` | `1.8.260209005` | `1.8.260416003` | Patch | Latest 1.8.x servicing patch (2026-04-21). 1.8 is in **Maintenance** support until 2026-09-09 — patches only, critical fixes. Same-minor bump = zero API surface change. |

### Updated packages — explicitly NOT recommended for v1.5

| Package | Current | Available | Why deferred |
|---------|---------|-----------|--------------|
| `Microsoft.WindowsAppSDK` | `1.8.x` | `2.0.1` | **Major version jump** released 2026-04-29 (8 days before milestone start). 1.8 still receives maintenance patches through 2026-09-09. v1.5 mixes feature parity + hardening + code-review remediation — adding a major SDK swap multiplies regression surface for zero parity gain. Defer to v1.6 or align with the `V2-05: Migration to .NET 10 LTS` future item from PROJECT.md. |
| `CommunityToolkit.Mvvm` | `8.4.x` | none | No 8.5.x exists yet; 8.4.2 is the current head. |

---

## Per-Cluster Stack Verdict

### Cluster A — macOS v1.12.0 Feature Parity

**A1: Next 5h-window start time label**
- **Stack delta: NONE.** Data already lives on `UsageResponse.FiveHour.ResetsAt` (per `backlog_next_window_start_label.md`). Pure XAML + ViewModel + new `.resw` keys (DE/EN).

**A2: Session renaming + persistence + Sessions Settings tab**
- **Stack delta: NONE.** Mirror the macOS implementation pattern using existing infrastructure.
- **Persistence mechanism — recommendation: dedicated JSON file `%LOCALAPPDATA%\CCInfoWindows\session-names.json`** behind a new `ICustomSessionNameStore` service. Justification:
  1. **macOS reference** uses `UserDefaults` keyed by `"session.customNames.v1"` storing JSON-encoded `[String: String]` (slug → name). Source: `ccInfo/ccInfo/Services/CustomSessionNameStore.swift` + `SessionRenameModel.swift` (verified via `gh api` against `stefanlange/ccInfo` main branch). The Windows-idiomatic equivalent of `UserDefaults` for non-secret app data is a JSON file under `%LOCALAPPDATA%`, exactly the pattern `SettingsService.cs` already establishes.
  2. **Why NOT expand `AppSettings.cs`:** the macOS author intentionally separated session names from app settings — different concerns, different lifetime, different prune semantics (`pruneOrphans(activeSlugs:)`). Mixing them into one `settings.json` blob means every settings save rewrites the entire session-name dictionary and vice versa, and complicates the v1.4 `MainWindow.OnClosing` flush path.
  3. **Why NOT a key-value store NuGet:** zero ecosystem benefit over `System.Text.Json` for `Dictionary<string, string>`. Adds dependency surface for a 50-line class.
  4. **Pattern alignment:** clones `SettingsService.cs` (LoadSessionNames / SaveSessionNames + atomic write + corrupt-file → empty dict + log). Add `IUsageHistoryService`-style `SemaphoreSlim` write guard if commits happen on the keystroke path; alternatively, debounce via `IDispatcherTimer` (already in stack).
  5. **Schema versioning:** include `"v": 1` envelope so the corrupt-blob recovery from macOS (drop & restart empty) is preserved.
  6. **Sanitization parity:** port the macOS sanitization rules (strip C0/C1, bidi overrides, zero-width formatting, line/paragraph separators; preserve U+200D ZWJ for emoji) — this is a security control (CVE-2021-42574 "Trojan Source" class), not a polish item.
- **Test seam:** `ICustomSessionNameStore` interface mirrors macOS `init(defaults:)` test seam — unit tests inject an in-memory fake; integration test uses a temp directory.

### Cluster B — Bug Hardening

**B1: Cwd hydration + configurable session-visibility window**
- **Stack delta: NONE.** Reuses existing `IJsonlService`, `ISettingsService`, `AppSettings`, `WinUI3Localizer` (new ComboBox + 4 dropdown labels in 2 locales).

**B2: Org-ID picker for multi-account users**
- **Stack delta: NONE.** This was the riskiest item on paper, but the heavy lift is already in the codebase.
- **Verified endpoint:** `GET https://claude.ai/api/organizations` returns a JSON array of org objects with `uuid` field. Already called by `ClaudeApiService.TryMigrateOrgIdAsync` (line 157-192) — the bug `backlog_org_id_picker.md` flags is that the resolver blindly takes `orgs[0]` instead of letting the user pick when more than one entry exists.
- **No documented "list all orgs" Admin API for web-app sessions exists** ([Admin API at `platform.claude.com`](https://platform.claude.com/docs/en/api/admin/organizations) requires `sk-ant-admin-…` keys, a different auth surface). The web-app `/api/organizations` is the only realistic source — and it's the one already in production. Confidence: HIGH (in-code proof of the endpoint working since v1.0).
- **Implementation:** lift `TryMigrateOrgIdAsync` into a public `IOrgResolverService.ListAvailableAsync()` returning `IReadOnlyList<OrgSummary>`; wire to a new Settings → Account dropdown; persist override via existing `ICredentialService.SaveOrganizationId` (the `claude-org` target already exists). Zero new dependencies.

**B3: Pricing-service silent-failure → UI banner**
- **Stack delta: NONE.** Surfaces existing `PricingService` exception via the `HasApiError` / banner channel `MainViewModel` already exposes for usage-fetch failures. M-2 (localize `LastFetchRelativeTime`) bundles cleanly here — same surface, same `WinUI3Localizer` keys.

### Cluster C — v1.4 Code-Review Remediation

**Stack delta: NONE — explicitly confirmed.** This cluster is pure refactoring.

| Item | Stack impact | Notes |
|------|-------------|-------|
| C-1 | None | Wrap fire-and-forget `Task` with `try/catch` + log. Zero new APIs. |
| C-2 | None | New **interface only** (`IDispatcherQueue`) + adapter — see "DispatcherQueue adapter" subsection below. |
| M-1 | None | Delete `LogoutRequestedMessage.cs`. |
| M-2 | None | Add 2 `.resw` keys (DE/EN); `WinUI3Localizer` already in stack. |
| M-3 | None | Restore real default value to `_contextModelBadgeColor`. |
| Nits ×3 | None | Opportunistic. |

#### C-2 DispatcherQueue adapter — design verdict

**Recommendation: introduce `IDispatcherQueue` adapter mirror of `IDispatcherTimer`.** Justification:

1. **Pattern already proven.** `IDispatcherTimer` + `WinuiDispatcherTimerAdapter` (v1.4, About-tab) is the canonical template — 6 lifecycle tests went GREEN with `FakeDispatcherTimer`. C-2 is the same problem class: a WinRT type that's impossible to fake in headless tests because it requires a UI thread context.
2. **Inline pattern fails the test gate.** Today, `MainViewModel` calls `_dispatcherQueue?.TryEnqueue(...)` directly. Asserting "C-2 fix marshals to the UI thread" via xUnit requires a way to verify `TryEnqueue` was invoked AND with the right delegate. With a concrete `DispatcherQueue` field, that assertion is impossible without spinning a real WinUI dispatcher in test (which is exactly what `IDispatcherTimer` was created to avoid).
3. **Surface is tiny.** `IDispatcherQueue { bool TryEnqueue(Action callback); bool HasThreadAccess { get; } }` covers every use site grepped from `ViewModels/`. An adapter wrapping `Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()` is ~15 lines.
4. **Standardization opportunity.** Per `architecture_weakreferencemessenger_with_transient_vms.md` Pitfall #2 ("RULE"), every `IRecipient<T>.Receive` that touches UI state must marshal. The interface gives that rule a single audit point — a `FakeDispatcherQueue.AssertAllEnqueuedCallbacksRan()` becomes the test that locks the rule in place across the codebase, not just the C-2 fix site.
5. **Lifetime parity.** Register as `AddSingleton<IDispatcherQueue>` (DI-resolved at composition root from `App.OnLaunched`'s UI thread) — sidesteps the AddTransient + WeakReferenceMessenger GC trap documented in the same memory file.

**Reject alternative:** wrapping `_dispatcherQueue?.TryEnqueue(...)` calls inline. Lower up-front cost but leaves C-2's test ungate-able and recreates the v1.4 mistake of "let's just inline it for now" that became the WeakReferenceMessenger logout regression.

---

## Version Notes (Context7 + NuGet-verified)

| Library | Current code | Latest stable | Source | Confidence |
|---------|-------------|---------------|--------|------------|
| `CommunityToolkit.Mvvm` | 8.4.0 | **8.4.2** (2026-03-25) | [NuGet Gallery](https://www.nuget.org/packages/CommunityToolkit.Mvvm) | HIGH |
| `Microsoft.WindowsAppSDK` (1.8 line) | 1.8.260209005 | **1.8.260416003** (2026-04-21) | [WinAppSDK release channels](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-channels) | HIGH |
| `Microsoft.WindowsAppSDK` (overall) | — | 2.0.1 (2026-04-29, **defer**) | Same | HIGH |
| WinAppSDK 1.8 support window | — | Maintenance until **2026-09-09** | Same | HIGH |
| WinAppSDK 2.0 support window | — | Current until 2027-04-29 | Same | HIGH |
| `Microsoft.Graphics.Win2D` | 1.3.2 | (not re-verified — no v1.5 chart work planned) | — | n/a |
| `WinUI3Localizer` | 2.3.0 | (not re-verified — no library-touching work) | — | n/a |
| `AdysTech.CredentialManager` | 3.1.0 | (not re-verified — no credential-API work) | — | n/a |

**Endpoint verification:**
- `GET https://claude.ai/api/organizations` returns array of `{uuid, ...}`. Confirmed in `ClaudeApiService.cs:163`. Confidence: HIGH (in-code proof of working production call since v1.0).
- macOS persistence pattern: `UserDefaults` key `"session.customNames.v1"` storing JSON `[String: String]`, slug → name, with sanitization (CVE-2021-42574 control-char strip), atomic encode-then-mutate, and orphan prune. Source: [`ccInfo/ccInfo/Services/CustomSessionNameStore.swift`](https://github.com/stefanlange/ccInfo/blob/main/ccInfo/ccInfo/Services/CustomSessionNameStore.swift) + `SessionRenameModel.swift` (read via `gh api`). Confidence: HIGH.

---

## Installation / Migration Steps

```xml
<!-- CCInfoWindows.csproj — patch-version bumps for v1.5 -->
<PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.2" />
<PackageReference Include="Microsoft.WindowsAppSDK" Version="1.8.260416003" />
```

```bash
# After editing csproj
dotnet restore CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj
dotnet build  CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj
# Run existing test suite — both bumps are same-minor patches; expect zero breakage
dotnet test
```

**No new files in `Resources/` or `runtimes/`. No installer (Inno Setup) script changes.**

---

## Sources

- macOS reference (verified by `gh api`):
  - [`ccInfo/ccInfo/Services/CustomSessionNameStore.swift`](https://github.com/stefanlange/ccInfo/blob/main/ccInfo/ccInfo/Services/CustomSessionNameStore.swift)
  - [`ccInfo/ccInfo/Services/SessionRenameModel.swift`](https://github.com/stefanlange/ccInfo/blob/main/ccInfo/ccInfo/Services/SessionRenameModel.swift)
  - [`stefanlange/ccInfo` releases](https://github.com/stefanlange/ccInfo/releases)
- NuGet & SDK channels:
  - [`CommunityToolkit.Mvvm` on NuGet](https://www.nuget.org/packages/CommunityToolkit.Mvvm)
  - [`Microsoft.WindowsAppSDK` on NuGet](https://www.nuget.org/packages/Microsoft.WindowsAppSDK)
  - [Windows App SDK release channels (Microsoft Learn)](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-channels)
- Claude.ai API surface:
  - In-code proof: `CCInfoWindows/Services/ClaudeApiService.cs:163` — `await _bridge.FetchJsonAsync($"{BaseUrl}/api/organizations")`
  - [Admin API (different surface — sk-ant-admin keys, NOT applicable)](https://platform.claude.com/docs/en/api/admin/organizations)
- In-house architectural memory:
  - `architecture_weakreferencemessenger_with_transient_vms.md` — Pitfall #2 (thread marshaling rule) drives the C-2 adapter recommendation.
  - `backlog_org_id_picker.md` — B2 problem statement.
  - `backlog_next_window_start_label.md` — A1 data source.
  - `backlog_pricing_never_loaded.md` — B3 problem statement.
