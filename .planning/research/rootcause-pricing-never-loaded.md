---
name: Backlog — Pricing service never loads (timestamp shows "Never")
description: The About/Updates settings tab shows "Last fetched: Never" / "Nie aktualisiert" instead of a real timestamp. This indicates `_pricingService.EnsurePricesLoadedAsync()` is either failing silently (catch-all in MainViewModel.cs:368-370) or has been disabled. Worth diagnosing — also blocks observation of the Phase 22 DispatcherTimer.
type: project
originSessionId: 4fcfe4f9-d257-456b-bc4f-1109b37175ac
---
# Pricing service never loads — timestamp shows "Never"

**Reported:** 2026-05-07 by user during v1.4 UAT (Phase 22 Test 4).

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
