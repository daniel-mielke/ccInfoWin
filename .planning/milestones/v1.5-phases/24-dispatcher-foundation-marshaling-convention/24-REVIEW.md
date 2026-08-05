---
phase: 24
status: findings_found
findings_count: 7
critical_count: 0
warning_count: 4
info_count: 3
generated_at: 2026-05-08T14:45:22Z
depth: standard
files_reviewed: 11
files_reviewed_list:
  - CCInfoWindows/CCInfoWindows/Services/Interfaces/IDispatcherQueue.cs
  - CCInfoWindows/CCInfoWindows/Services/Interfaces/ThreadSafeReceiveAttribute.cs
  - CCInfoWindows/CCInfoWindows/Services/WinuiDispatcherQueueAdapter.cs
  - CCInfoWindows/CCInfoWindows/App.xaml.cs
  - CCInfoWindows/CCInfoWindows/MainWindow.xaml.cs
  - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
  - CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj
  - CCInfoWindows.Tests/Convention/MessengerThreadingConventionTests.cs
  - CCInfoWindows.Tests/Helpers/FakeDispatcherQueue.cs
  - CCInfoWindows.Tests/ViewModels/MainViewModelAuthFlowTests.cs
  - CCInfoWindows.Tests/ViewModels/MainViewModelRefreshTests.cs
---

# Phase 24: Code Review Report

**Reviewed:** 2026-05-08T14:45:22Z
**Depth:** standard
**Files Reviewed:** 11
**Status:** findings_found

---

## Executive Summary

Phase 24 liefert eine solide Dispatcher-Abstraktions-Infrastruktur. Die Kernziele (L-04 always-TryEnqueue, C-1/C-2-Fix, CD-04 UnregisterAll, CD-05 Window-Exemption) sind korrekt umgesetzt. Keine Critical-Befunde — der Code ist shippable. Es gibt jedoch vier Warnings: ein hartkodierter deutscher String in einem lokalisierungskonformen Codebase (UpdateMessage), eine unnötige Wrapper-Lambda in `WinuiDispatcherQueueAdapter.TryEnqueue`, eine Diskrepanz zwischen `MainViewModelTestHarness.ApplyStatistics` und der Produktionsmethode (filtert `<synthetic>`/`unknown` Models nicht), und ein fehlender `InvocationCount`-Reset in `FakeDispatcherQueue.Pump()`. Drei Info-Befunde (Sanity-Assertion zu stark, redundanter `using`-Import im Test, lokaler `DispatcherQueue.GetForCurrentThread()`-Aufruf in `CopyChartToClipboard`).

---

## Warnings

### WR-01: Hartkodierter deutscher String `"Update v{version} verfügbar"` verletzt Lokalisierungskonvention

**File:** `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs:989`
**Issue:** `OnUpdateAvailable` setzt `UpdateMessage = $"Update v{version} verfügbar";` — ein hartkodierter deutscher String direkt im ViewModel. Das Projekt nutzt WinUI3Localizer mit `.resw`-Ressourcen für alle UI-Texte (vgl. `FormatBurnRateText` bei Zeile 525 als Gegenbeispiel). Diese Methode läuft auf dem UI-Thread via `_dispatcherQueue.TryEnqueue`, d.h. ein `Localizer.Get().GetLocalizedString(...)` wäre problemlos verwendbar. Phase 27 (L10N-01) wird diesen Bereich anfassen, aber die String-Konstante wurde in Phase 24 nicht als Out-of-Scope markiert — sie gehört zu den v1.4 Nits (CLEANUP-03 Scope).
**Fix:**
```csharp
// In OnUpdateAvailable:
_dispatcherQueue.TryEnqueue(() =>
{
    UpdateMessage = string.Format(
        Localizer.Get().GetLocalizedString("UpdateAvailableMessage"),
        version);
    IsUpdateAvailable = true;
});
```
Bis Phase 27 mindestens als TODO kommentieren:
```csharp
// TODO(Phase 27 L10N-01): localize this string via resw key "UpdateAvailableMessage"
UpdateMessage = $"Update v{version} verfügbar";
```

---

### WR-02: Unnötige Wrapper-Lambda in `WinuiDispatcherQueueAdapter.TryEnqueue` verdeckt Action-Identität

**File:** `CCInfoWindows/CCInfoWindows/Services/WinuiDispatcherQueueAdapter.cs:27`
**Issue:** `return _inner.TryEnqueue(() => action());` wickelt `action` in eine zweite Lambda ein, statt sie direkt weiterzugeben. Das ist nicht falsch, erzeugt aber eine überflüssige Closure-Allokation pro Enqueue-Aufruf. Wichtiger: Es ist eine Clean-Code-Verletzung (kein "Wrap external libraries" Problem, sondern unnötiger Indirection-Layer). Der eigentliche `.TryEnqueue` erwartet `DispatcherQueueHandler`, was `Action`-kompatibel ist. Die direkte Übergabe würde funktionieren. Die `ArgumentNullException.ThrowIfNull(action)` Guard-Zeile 26 rechtfertigt die Existenz der Methode, aber nicht die Lambda.
**Fix:**
```csharp
public bool TryEnqueue(Action action)
{
    ArgumentNullException.ThrowIfNull(action);
    return _inner.TryEnqueue(action);   // direkte Weitergabe — keine doppelte Closure
}
```

---

### WR-03: `MainViewModelTestHarness.ApplyStatistics` divergiert von Produktionsmethode — Test-Drift-Risiko

**File:** `CCInfoWindows.Tests/ViewModels/MainViewModelStatisticsTests.cs:119–130`
**Issue:** Die `MainViewModelTestHarness.ApplyStatistics`-Implementierung (Zeilen 119–130) ist eine **manuelle Kopie** der Produktionslogik, jedoch ohne die `<synthetic>`/`unknown`-Filterung (Zeilen 837–840 in `MainViewModel.cs`). Das bedeutet:

1. Ein Test mit `Models = ["<synthetic>"]` würde in der Harness einen Modellnamen ausgeben, in der Produktion aber `"–"` (Em-Dash).
2. Jede zukünftige Änderung an `MainViewModel.ApplyStatistics` muss manuell in der Harness nachgezogen werden — ein klassisches DRY-Verletzungsproblem.

Die Harness testet außerdem gar nicht die echte `MainViewModel.ApplyStatistics`-Methode (die `internal` ist und über `InternalsVisibleTo` zugänglich wäre), sondern eine eigene Re-Implementierung. Das macht die Tests wertlos für Regressionssicherheit der echten Methode.
**Fix:** Harness die echte `internal`-Methode aufrufen lassen:
```csharp
// Option A: Harness wraps a real MainViewModel (using FakeDispatcherQueue)
public class MainViewModelTestHarness
{
    private readonly MainViewModel _vm;

    public MainViewModelTestHarness(IJsonlService jsonlService, IPricingService pricingService)
    {
        // build minimal MainViewModel with all required mocks + FakeDispatcherQueue
        _vm = BuildMinimalVm(jsonlService, pricingService);
    }

    public void ApplyStatistics(StatisticsSummary stats) => _vm.ApplyStatistics(stats);

    public string StatisticsModels => _vm.StatisticsModels;
    // etc.
}
```
Alternativ, wenn der Harness-Ansatz beibehalten wird: zumindest die `Where`-Filter aus der Produktion 1:1 übernehmen und einen Test für `"<synthetic>"`-Filterung hinzufügen.

---

### WR-04: `FakeDispatcherQueue.Pump()` setzt `InvocationCount` nicht zurück — Assertions können verfälscht werden

**File:** `CCInfoWindows.Tests/Helpers/FakeDispatcherQueue.cs:37–46`
**Issue:** `InvocationCount` zählt jeden `TryEnqueue`-Aufruf kumulativ, auch im queued-Modus. `Pump()` drainiert die Queue, inkrementiert aber `InvocationCount` nicht für die ausgeführten Actions (was korrekt ist — `TryEnqueue` hat schon gezählt). Das Problem ist subtiler: Im queued-Modus (`ExecuteInline = false`) könnte ein Test `InvocationCount` nach `Pump()` abfragen und erwarten, dass er nur Aufrufe aus dem *aktuellen* Testabschnitt sieht — aber `InvocationCount` ist niemals rücksetzbar (kein `Reset()`-Methode). Wenn die Fake-Instanz zwischen Tests wiederverwendet wird (z.B. in Setup-Methoden), akkumuliert der Counter.

In der aktuellen Phase 24 wird keine `Reset()`-Methode benötigt, aber da `FakeDispatcherQueue` als wiederverwendbarer Test-Helper konzipiert ist (Kommentar sagt "Use for tests that need to assert ordering"), fehlt die `Reset()` API für Phase 25–27 Tests die Dispatcher-Invocation-Count assertions machen wollen.
**Fix:**
```csharp
/// <summary>Resets InvocationCount and drains any pending queued actions without executing them.</summary>
public void Reset()
{
    _queued.Clear();
    InvocationCount = 0;
}
```

---

## Info

### IN-01: Konvention-Test Sanity-Assertion `>= 4` ist fragil bei Assembly-Umstrukturierungen

**File:** `CCInfoWindows.Tests/Convention/MessengerThreadingConventionTests.cs:40–44`
**Issue:** `Assert.True(receivers.Count >= 4, ...)` — die Zahl 4 ist eine Magic Number, die nur für den Phase-24-Stand stimmt. Die CONTEXT-Entscheidung D-01 wählte diese Assertion explizit (vs. `Assert.NotEmpty` im ursprünglichen Plan-Sketch), was nachvollziehbar ist. Jedoch: wenn Phases 25–27 neue `IRecipient<>`-Handler hinzufügen, steigt die Zahl, aber der Test bleibt bei `>= 4` — er detektiert also nicht, wenn Handler *versehentlich entfernt* werden (solange ≥ 4 verbleiben). Das ist bewusst dokumentiert im Kommentar. Die Magic Number `4` sollte als benannte Konstante geführt werden.
**Fix:**
```csharp
private const int Phase24KnownReceiverCount = 4;

Assert.True(receivers.Count >= Phase24KnownReceiverCount,
    $"Expected at least {Phase24KnownReceiverCount} IRecipient<> Receive methods; found {receivers.Count}. ...");
```

---

### IN-02: Redundanter `using CommunityToolkit.Mvvm.Messaging` Import im Konvention-Test

**File:** `CCInfoWindows.Tests/Convention/MessengerThreadingConventionTests.cs:4`
**Issue:** `using CommunityToolkit.Mvvm.Messaging;` wird importiert, aber `IRecipient<>` wird nur über den vollständigen Pfad `typeof(IRecipient<>)` referenziert — der `using` ist nicht notwendig. Wenn der Namespace schon über `ImplicitUsings` oder einen anderen Test-Basis verfügbar ist, ist er doppelt. Erzeugt einen Compiler-Warning CS8019 (unnecessary using directive) — widerspricht dem "zero new compiler warnings"-Kriterium aus den PLANs.
**Fix:** `using CommunityToolkit.Mvvm.Messaging;` entfernen, da `IRecipient<>` über `typeof(IRecipient<>)` ohne Namespace-Qualifizierung ausgedrückt werden kann — oder den voll qualifizierten Typ `CommunityToolkit.Mvvm.Messaging.IRecipient<>` im Code verwenden und den `using` weglassen.

---

### IN-03: `CopyChartToClipboard`-Command ruft `DispatcherQueue.GetForCurrentThread()` direkt auf — nicht injiziert

**File:** `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs:971`
**Issue:**
```csharp
var winuiDispatcherQueue = DispatcherQueue.GetForCurrentThread();
await ExportHelper.CopyChartToClipboardAsync(winuiDispatcherQueue, ...);
```
Dieser Aufruf ist außerhalb des Phase-24-Scope (er berührt `ExportHelper`, nicht `IRecipient<>`), aber er ist ein WinRT-Typ-Leak ins ViewModel — genau das, was die IDispatcherQueue-Abstraktion verhindern soll. `ExportHelper.CopyChartToClipboardAsync` benötigt den WinRT `DispatcherQueue` für `Clipboard.SetContent`-Marshaling. Das ist ein bekanntes Muster im Codebase (analoger Aufruf bei `InitializeAsync` Zeile 397 für `CreateTimer()`). Für Phase 24 ist es Out-of-Scope, aber das Muster zeigt, dass `IDispatcherQueue` noch nicht vollständig alle WinRT-DispatcherQueue-Verwendungen im ViewModel ablöst — das ist für zukünftige Phasen zu beachten.
**Empfehlung:** Für Phase 25/26 Planung: `ExportHelper.CopyChartToClipboardAsync`-Signatur auf `IDispatcherQueue` umstellen oder den direkten Aufruf mit einem `// Phase 24 out-of-scope: ExportHelper requires WinRT DispatcherQueue for Clipboard marshaling`-Kommentar markieren.

---

## Strengths

**Architektur:** Die `IDispatcherQueue`-Abstraktion folgt dem `IDispatcherTimer`-Präzedenzfall exakt (L-09 compliant). `WinuiDispatcherQueueAdapter` als `internal sealed class` verhindert unbeabsichtigte Subklassierung. Die Null-Guard im Konstruktor mit sprechendem `InvalidOperationException`-Text ist produktionsreif.

**Threading-Korrektheit:** L-04 (always-TryEnqueue) ist korrekt umgesetzt. `Receive(AuthStateChangedMessage)` hat genau eine Statement: den `TryEnqueue`-Aufruf. Kein `if (!HasThreadAccess)`-Shortcut. CD-04 (`UnregisterAll` als erste Anweisung in `InitializeAsync`) ist korrekt platziert und mit Re-Registrierung gepaart. CD-05 #4 (RefreshIntervalChangedMessage-Lambda) ist defensiv in TryEnqueue gewrapped.

**Constructor Injection (CD-01):** `_dispatcherQueue` ist `readonly` und nicht-null nach Konstruktion. Alle `?.`-Null-Conditions wurden entfernt. Keine cold-path null risk (PITFALLS C2-P2 eliminiert).

**Konvention-Test:** `MessengerThreadingConventionTests` ist robust konzipiert. Die IL-Scan-Logik mit `Module.ResolveMethod` und Exception-Handling für ungültige Tokens ist korrekt. Die `DeclaringType`-Prüfung (Interface direkt ODER implementierender Typ) ist notwendig und vorhanden. Window-Subklassen-Filter (CD-05 #3 option b) ist korrekt implementiert.

**FakeDispatcherQueue:** Inline- und Queued-Modus, `HasThreadAccess`-Override, `InvocationCount`-Tracking und `PendingActions`-Exposed-Queue decken alle bekannten Test-Szenarien ab.

**Scope-Disziplin:** Pricing fire-and-forget (Zeilen 378–382) wurde korrekt nicht angetastet (Phase 27 PRICING-01). G-2/G-3 wurden nicht eingeführt. WinAppSDK 2.0 und Roslyn-Analyzer sind korrekt deferred.

---

_Reviewed: 2026-05-08T14:45:22Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
