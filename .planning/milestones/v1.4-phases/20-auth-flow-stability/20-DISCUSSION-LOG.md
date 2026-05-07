# Phase 20: Auth Flow Stability - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-05-06
**Phase:** 20-auth-flow-stability
**Areas discussed:** 401 Detection Routing, Reload Button Placement, Sign-Out Reset Mechanism, Background Window Activation

---

## 401 Detection Routing

### Question 1: Wo soll der erste 401 erkannt werden und das _autoReauthAttempted-Flag geprüft/gesetzt werden?

| Option | Description | Selected |
|--------|-------------|----------|
| In Receive(AuthStateChangedMessage) | Handler erweitern: bei message.Value==false prüft er _autoReauthAttempted. Beim ersten Mal: Flag setzen, NavigateTo<LoginView>(), kein InfoBar. Zweites Mal: bestehender Pfad (IsSessionExpired=true). | ✓ |
| In MainViewModel.PollUsageAsync direkt | UnauthorizedAccessException bis MainViewModel durchreichen, dort im catch-Block das Flag prüfen. Refactor von ClaudeApiService nötig. | |
| WebViewBridge wirft HttpFetchException(401) | Spec wörtlich umsetzen — größter Refactor, berührt mehrere Schichten. | |
| Neue dedizierte SessionExpiredMessage | AuthStateChangedMessage(false) bleibt für expliziten Logout, neue Message für 401-Auto-Reauth. Mehr Boilerplate. | |

**User's choice:** In Receive(AuthStateChangedMessage)
**Notes:** Recommended option — minimaler Eingriff, nutzt vorhandenen Message-Pfad. WebViewBridge und ClaudeApiService bleiben unverändert. Spec-Drift (HttpFetchException(401) vs. UnauthorizedAccessException) wird zugunsten der bestehenden Architektur aufgelöst.

### Question 2: Welche Reset-Trigger soll _autoReauthAttempted = false haben?

| Option | Description | Selected |
|--------|-------------|----------|
| HTTP 200 + Logout + Login-Success | Reset (a) im Success-Path von PollUsageAsync, (b) im Logout-Command, (c) im neuen AuthStateChangedMessage(true)-Handler. Konstruktor-Default für App-Restart. | ✓ |
| Nur HTTP 200 + Login-Success | Logout-Reset weglassen weil Logout ohnehin Re-Navigation auslöst. | |
| Nur HTTP 200 (minimal) | Login-Success-Reset auch weglassen. | |
| Flag wird gar nicht resettet | Verlassen auf Transient-Lifetime allein. | |

**User's choice:** HTTP 200 + Logout + Login-Success
**Notes:** Spec-konform, belt-and-suspenders Ansatz. Verteidigt die Invariante explizit, auch wenn Transient-Lifetime einige Resets redundant macht.

### Question 3: Wie soll der Receive-Handler bei AuthStateChangedMessage(true) reagieren?

| Option | Description | Selected |
|--------|-------------|----------|
| Sofort RefreshUsageCommand.ExecuteAsync | Handler ruft RefreshUsageCommand.ExecuteAsync(null) auf, setzt Flags zurück. Spec FEAT-07b konform. | ✓ |
| Nur State zurücksetzen, nächster Poll holt Daten | Nutzer wartet bis zum nächsten regulären Poll-Tick. Schlechtere UX. | |
| Refresh + Cache-Reload | Plus expliziter LoadCacheAsync()-Call für instant feedback. Doppelter Code-Pfad. | |

**User's choice:** Sofort RefreshUsageCommand.ExecuteAsync
**Notes:** Genau wie Spec FEAT-07b vorschreibt — sofortige Datenaktualisierung ohne App-Restart.

---

## Reload Button Placement

### Question 1: Wo soll der Reload-Button in LoginView platziert werden?

| Option | Description | Selected |
|--------|-------------|----------|
| Top-Right-Overlay über WebView2 | HorizontalAlignment=Right, VerticalAlignment=Top, Margin=8, transparenter Hintergrund. Spec-konform. | ✓ |
| Eigene schmale Header-Leiste über WebView2 | Grid mit Row=Auto + Row=*. Stiehlt vertikalen Platz. | |
| Bottom-Right-Overlay | Weniger entdeckbar, weicht von Spec ab. | |
| Top-Right-Overlay mit halbtransparentem Backdrop | Visuell schwerer für selten genutzten Recovery-Button. | |

**User's choice:** Top-Right-Overlay über WebView2
**Notes:** Matches macOS reference and spec exactly.

### Question 2: Welchen Glyph und Stil soll der Reload-Button haben?

| Option | Description | Selected |
|--------|-------------|----------|
| Spec-Default: E72C, 14px, transparent | Spec-Wortlaut: FontSize=14. | |
| Gleicher Stil wie MainView-Footer-Refresh | FontSize=16, Glyph=E72C. Visuell identisch zum Hauptfenster. | ✓ |
| Reload-Glyph statt Refresh-Glyph | E895 (Sync) statt E72C — semantisch unterschiedlich. | |

**User's choice:** Gleicher Stil wie MainView-Footer-Refresh
**Notes:** Konsistenz schlägt 2px Größenunterschied. User priorisiert visuelle Einheitlichkeit über Spec-Wortlaut bei Stil-Detail.

---

## Sign-Out Reset Mechanism

### Question 1: Wo soll der WebView2-Reset zur Login-URL nach Logout passieren?

| Option | Description | Selected |
|--------|-------------|----------|
| WebView2 ausblenden bis Navigate fertig | Visibility=Collapsed initial, im NavigationCompleted-Handler auf Visible flippen wenn login-URL geladen. ProgressRing deckt Übergang. 100% kein Flash. | ✓ |
| Pre-Navigate in Logout() | _bridge.NavigateLoginToLoginUrl() vor NavigateTo<LoginView>. Konflikt mit _bridge.Reset(). | |
| about:blank-Navigate in LoginView.Loaded | CoreWebView2 oft noch null bei Loaded — Race-Condition. | |
| Spec-Variante: Navigate in OnLoginViewLoaded | Löst Aufflackern nicht wirklich. | |

**User's choice:** WebView2 ausblenden bis Navigate fertig
**Notes:** Stärkste Garantie gegen Aufflackern. Bestehender Loading-Overlay wird wiederverwendet.

### Question 2: Wann genau soll WebView2 auf Visibility=Visible geschaltet werden?

| Option | Description | Selected |
|--------|-------------|----------|
| NavigationCompleted + Source ist Login-URL | args.IsSuccess==true UND sender.Source startet mit https://claude.ai/login. | ✓ |
| NavigationCompleted (egal welche URL) | Race-Risiko bei stale Cookies. | |
| Nach Cookie-Löschung + 100ms Delay | Timing-basiert, fragil. | |
| Sofort sichtbar (kein Hide) | Overlay-Z-Order-Problem, könnte alte Seite hinter Overlay zeigen. | |

**User's choice:** NavigationCompleted + Source ist Login-URL
**Notes:** Präzise Bedingung — nur wenn die echte Login-Form geladen ist, wird sie gezeigt.

---

## Background Window Activation

### Question 1: Wie soll das Aktivieren des Fensters bei NavigateTo<LoginView>() implementiert sein?

| Option | Description | Selected |
|--------|-------------|----------|
| Global in NavigationService.NavigateTo | App.MainWindow?.Activate() vor _frame.Navigate für jede Navigation. Eine Stelle. | ✓ |
| Nur im Auto-Reauth-Pfad in MainViewModel | Logik in zwei Schichten verteilt. | |
| Neue NavigateAndActivate<TPage>() | Boilerplate, Risiko falscher Methodenwahl. | |
| Nur wenn Window minimiert ist | WinUI 3 Presenter-API komplexer, kleines Edge-Case-Risiko. | |

**User's choice:** Global in NavigationService.NavigateTo
**Notes:** Sauberer Single-Point-of-Change. Activate() bei Foreground-Navigation hat Cost=0.

---

## Claude's Discretion

- Exact `IsLoading` extension shape (rename vs. invert)
- Defensive `NavigationFailed` timeout fallback
- Test-mock strategy for `WeakReferenceMessenger.Default`
- Order of `Logout()` side effects (current order accepted unless tests reveal issues)

## Deferred Ideas

- Test-strategy for `WeakReferenceMessenger.Default` mocking — deferred to planner
- `TryMigrateOrgIdAsync` 401-double-trigger edge case — flagged for code review
- `NavigationFailed` offline-fallback timeout — reload button is enough for v1.4
- Per-401-counter instead of single bool — settled by spec design decision #1
- `NavigateAndActivate<TPage>()` API shape — rejected in favor of global activation
