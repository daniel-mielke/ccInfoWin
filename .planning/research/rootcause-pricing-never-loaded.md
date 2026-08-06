---
name: Backlog — Pricing service never loads (timestamp shows "Never")
description: The About/Updates settings tab shows "Last fetched: Never" / "Nie aktualisiert" instead of a real timestamp. This indicates `_pricingService.EnsurePricesLoadedAsync()` is either failing silently (catch-all in MainViewModel.cs:368-370) or has been disabled. Worth diagnosing — also blocks observation of the Phase 22 DispatcherTimer.
type: project
originSessionId: 4fcfe4f9-d257-456b-bc4f-1109b37175ac
---
# Pricing service never loads — timestamp shows "Never"

**Reported:** 2026-05-07 by user during v1.4 UAT (Phase 22 Test 4).
**Status:** partly resolved — see "Status 2026-08-06". Still open, do not close.

## Status 2026-08-06 (after the full-repo review remediation, findings 34 and 15)

The 2026-05-07 report asked for a diagnosis and got blocked by the absence of a log. That part is fixed;
the reason the fetch fails on this machine is still unknown, and the user-facing banner is still dead.

**Now diagnosable — verified against `Services/LiteLLMPricingService.cs`:**

- Every failure path writes to `%LOCALAPPDATA%\CCInfoWindows\app.log` via `AppLog`, with a distinct source tag
  per path: `LiteLLMPricingService.LiveFetch`, `.LocalCache`, `.EmbeddedResource`, `.CacheWrite`. The
  "run it under a debugger and watch `Debug.WriteLine`" step in "How to diagnose" below is obsolete — read the
  log instead. It survives in Release, which `Debug.WriteLine` never did.
- A well-formed response yielding **zero** Anthropic entries now counts as a failure
  (`TryLoadFromLiveApiAsync` returns false and keeps the previous table) instead of publishing an empty map as
  `PricingSource.Live`. That was the reachable trigger for a total pricing failure that the About tab reported
  as healthy.
- A genuinely empty table is published as `PricingSource.Unknown`, not `PricingSource.Fallback`. The About tab
  therefore reads "Unknown" / "Unbekannt" instead of asserting "Fallback (bundled)" while every entry priced
  at `~$0.00`. `SettingsViewModel.PricingSourceText` resolves both labels through resw keys now, so this is
  visible in German too.
- `LastFetch` is still only stamped for `PricingSource.Live`, which is why the original symptom is "Never" and
  not a stale timestamp: on this machine the live fetch has never succeeded. That is now a one-line answer from
  `app.log` rather than a debugging session.

**Still open:**

- **The banner's trigger.** Up to and including the two landed waves, `MainViewModel` set `IsPricingError`
  from a `catch` around `_pricingService.EnsurePricesLoadedAsync()` — and that method cannot throw, because
  every loader catches internally. Requirements PRICING-01..03 and their InfoBar were therefore unreachable in
  production for the whole v1.5–v1.6 span. The correct trigger is `_pricingService.Source == PricingSource.Unknown`,
  which wave 1 made reachable. That rewiring (`MainViewModel.ApplyPricingSource`) is in flight in the wave
  running at the time of writing — present in the working tree, not yet in a commit. **Verify it is committed
  before treating PRICING-01 as met**, and confirm the banner actually appears with an empty pricing table.
- The underlying question from 2026-05-07 — *why* does the live fetch fail here — is unanswered. Next step is
  now cheap: run the Release build, then read `app.log` for a `LiteLLMPricingService.LiveFetch` entry.
- Phase 22 POLISH-07 (About-tab timestamp ticking) is still unverifiable visually for the same reason: without
  a successful live fetch there is no changing timestamp to render.

## Symptom

Settings → About / Updates tab shows the pricing timestamp as "Never" / "Nie" — there has never been a successful pricing fetch in this app instance. Cost analytics (Phase 5) likely uses pricing data; if pricing is never loaded, the cost columns may show 0 or fall back to defaults.

## Where to investigate

- `MainViewModel.cs:366-370` starts the load fire-and-forget:
  ```csharp
  _ = Task.Run(async () =>
  {
      try { await _pricingService.EnsurePricesLoadedAsync(); }
      catch (Exception ex) { Debug.WriteLine($"[MainViewModel] Pricing load failed: {ex.Message}"); }
  });
  ```
  The catch-all swallows the exception. Without a debugger or log file, the actual failure is invisible.
- `IPricingService` / `LiteLLMPricingService` (referenced in Phase 21 PATTERNS as the SemaphoreSlim analog source) — check the LiteLLM endpoint, network behavior, and the JSON path it caches to.
- `SettingsViewModel.LastFetchRelativeTime` — the display surface; if `_pricingService.LastFetch` is null, it returns "Never" (or the localized equivalent).

## How to diagnose

1. Run the app under `dotnet run` and watch the console — Debug.WriteLine output may surface the catch-all error message.
2. Check `%LOCALAPPDATA%\CCInfoWindows\` for a pricing cache file (e.g. `pricing.json` or similar) — if absent, load has never persisted; if stale, fetch is succeeding but timestamp tracking is broken.
3. Test network access to the LiteLLM endpoint manually (curl) to rule out connectivity issues.
4. If failure is consistent, propagate the catch-all error into `HasApiError` / a separate banner so users know pricing is unavailable.

## Why this matters

- Cost analytics (Phase 5) silently degrades to "no data" without user feedback.
- Phase 22 POLISH-07 (About-tab timestamp ticking) cannot be visually verified — DispatcherTimer is wired correctly but never has changing data to render.
- User loses trust in the displayed numbers if they spot "Never" without context.

## Verify before scoping

- Confirm whether `LiteLLMPricingService` is the active implementation or whether DI binds something else.
- Confirm the cache file path and whether it exists / has stale content.
- Confirm the Debug.WriteLine output captures the actual exception type (could be HttpRequestException, JsonException, or filesystem).
