# Backlog

Pending work not yet assigned to a milestone.
GSD: promote via `/gsd-review-backlog`. Plan mode / ultracode: read this first.

## Open

### Carried out of v1.6 (deliberately not done there)

- **`MainViewModel` split** — 1235 lines, 48 observable properties. 5h / Weekly / Sonnet /
  Context / Statistics / Banners are clean groups. Kept out of v1.6 because a refactor that size
  next to seven feature ports makes regressions unfindable.
- **Tier-trigger semantics for long-context pricing** (`JsonlService`, `cumulativeBefore`) —
  the running input sum is per model *over the aggregation period*, not the size of the
  individual request, so a week that crosses 200k input switches every later entry to
  long-context prices regardless of request size. Probably identical upstream. Documented in
  v1.6 so task 5-4 would not silently cement it; still open.
- **Chart height 160** — derived, but ultimately taste. Revisit after the first visual UAT;
  it is one number in `MainView.xaml` plus two in `ExportHelper`.
- **v1.6 visual UAT** — done 2026-08-06, U1–U11 all pass. Record in `.planning/STATE.md`.
  Caveat: that record predates the 2026-08-06 review remediation, see the STATE.md section
  "Review-Remediation 2026-08-06".

### Deferred out of the 2026-08-06 review remediation

- **Single-instance guard** — there is none anywhere in the app (`grep -r "Mutex\|AppInstance"` over
  `CCInfoWindows/` returns zero hits), so nothing stops a second process from launching. That matters because
  the WebView2 user-data folder is exclusive: the second instance's
  `CoreWebView2Environment.CreateWithOptionsAsync` throws while holding nothing useful, which was one of the two
  triggers behind review finding 4 (the invisible bootstrap failure). Finding 4's own fix gives that failure a
  visible surface and a log entry; it does not stop the second instance from happening.
  Options: (a) `Microsoft.Windows.AppLifecycle.AppInstance.FindOrRegisterForKey(key)` +
  `RedirectActivationToAsync(...)` in `App.OnLaunched` — the Windows App SDK 1.8 mechanism already referenced by
  the project, handles activation redirection properly, and works unpackaged, but `OnLaunched` is `async void`
  today so the redirect has to happen before any window is created; or (b) a named `Mutex`
  (`Global\CCInfoWindows` / `Local\…`) checked at startup — three lines, no SDK surface, but it can only
  *refuse* to start, it cannot hand the activation to the running instance, and it needs a `try/finally` release
  plus `AbandonedMutexException` handling.
  **Open product question:** should a second launch focus the already-running window (option (a), the
  Windows-native expectation for a monitoring app the user re-clicks from the taskbar) or refuse to start with a
  message (option (b))? Decide this before implementing — it selects the mechanism.

- **`crash.log` has no size cap** — `App.AppendToCrashLog` is a plain `File.AppendAllText` and
  `App.OnUnhandledException` sets `e.Handled = true` unconditionally (deliberately: for a usage monitor a
  process that survives a failed background tick beats one that vanishes mid-session). The combination means a
  tight exception loop — a timer tick that throws every 30 s, a binding that throws on every layout pass — grows
  `crash.log` without bound. `AppLog` solved exactly this problem next door: `AppLog.MaxLogBytes` is 1 MiB with
  a single roll to `app.log.1`, and every unhandled exception already lands there too.
  Options: (a) fold `crash.log` into `app.log` entirely and delete `AppendToCrashLog` — one sink, one cap, but
  it breaks the "send me your crash.log" habit and mixes handled with fatal entries; or (b) reuse `AppLog`'s
  roll mechanism for the crash file, which means extracting the cap/roll helper so both files share it.
  Do not invent a third rotation scheme.

## Notes

Root-cause research for shipped items: `.planning/research/rootcause-*.md`.
Changelog: `.planning/MILESTONES.md`.

