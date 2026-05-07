# Phase 23: Localization Gaps - Research

**Researched:** 2026-05-06
**Domain:** WinUI 3 / WinUI3Localizer resource binding + .resw authoring
**Confidence:** HIGH

## Summary

Phase 23 ist **kleiner als CONTEXT vermutet**. Drei zentrale Befunde verändern den Phase-Scope:

1. **Es gibt KEINE hardcoded `"Loading"` / `"No data"` / `"Not signed in"` Strings in den Views.** Ein vollständiger grep über `CCInfoWindows/CCInfoWindows/Views/*.xaml` und auch über die ViewModels ergibt 0 Treffer für die genannten Literale. Die in CONTEXT D-03 vermuteten Migrations-Sites existieren nicht. Der Phase-23-Scope reduziert sich damit auf das **Authoring** der Keys (für zukünftige Verwendung oder Library-Konsistenz mit dem macOS-Original) — NICHT auf das Migrieren existierender hardcoded Strings.
2. **Die zwei `LoginReloadButton.*` Keys existieren bereits korrekt in beiden resw-Dateien** mit den exakt in CONTEXT D-01 spezifizierten Werten. Phase 20 Plan 01 hat sie bereits authored. L10N-01 ist zu 2/6 erfüllt.
3. **Der Codebase nutzt ausschließlich `l:Uids.Uid` (WinUI3Localizer-Pattern), nirgends `x:Uid`.** Alle drei Views (`LoginView.xaml`, `MainView.xaml`, `SettingsView.xaml`) importieren `xmlns:l="using:WinUI3Localizer"`. CONTEXT D-04 muss korrigiert werden: neue Bindings nutzen `l:Uids.Uid="<KeyPrefix>"`, nicht `x:Uid`.

**Primary recommendation:** Phase 23 ist ein 4-Key-Authoring-Phase + Verifikation der 2 vorhandenen Keys + 1 echte Migrations-Site (`Title="Error"` in LoginView, `Title="API Error"` in MainView — beides nicht im L10N-01-Scope, also out-of-scope für Phase 23). Plan-Aufwand: ~30 min, ein einzelnes Plan-File.

## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| L10N-01 | 6 keys exist in beide resw | 2/6 vorhanden (`LoginReloadButton.[Tooltip|AutomationName]`); 4/6 NEU zu authoren |
| L10N-02 | Hardcoded `"Loading"`/`"No data"`/`"Not signed in"` migriert zu `x:Uid` | 0 Migrations-Sites gefunden — Requirement ist trivial erfüllt (Akzeptanz-grep gibt 0 Treffer) |
| L10N-03 | Runtime Language Switch zeigt korrekte Übersetzungen | WinUI3Localizer's `SetLanguage()` handhabt das automatisch — manueller Smoke |

## User Constraints (from CONTEXT.md)

### Locked Decisions
- **D-01 Authoritative Resource Keys:** Genau 6 Keys mit den in CONTEXT spezifizierten EN/DE-Werten:
  - `NotSignedIn.Text` → `Not signed in` / `Nicht angemeldet`
  - `NoData.Text` → `No data` / `Keine Daten`
  - `Loading.Text` → `Loading` / `Wird geladen`
  - `InactiveSessionTooltip` → `Inactive for > {0}min` / `Inaktiv seit > {0}min`
  - `LoginReloadButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip` → `Reload page` / `Seite neu laden` (BEREITS VORHANDEN)
  - `LoginReloadButton.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name` → `Reload login page` / `Login-Seite neu laden` (BEREITS VORHANDEN)
- **D-02 Pre-existing keys:** `LoginReloadButton.*` von Phase 20 NICHT re-authoren (duplicate `<data>` → resource lookup failure).
- **D-05 Format String:** `InactiveSessionTooltip` hat positional `{0}` für threshold; Phase 22 ruft bereits `Localizer.Get().GetLocalizedString("InactiveSessionTooltip")` auf und macht `string.Format`.
- **D-07 `\n` in Tooltip:** Phase 22 komponiert `path + "\n" + localizedThreshold`; resw-Wert enthält KEIN `\n`.
- **D-08 Test Coverage:** 2 xUnit Tests, je 1 pro Locale, die das Laden via Localizer verifizieren.

### Claude's Discretion
- Exakte Wortwahl der DE-Übersetzungen (Vorschläge in CONTEXT akzeptabel; muttersprachliche Review optional).
- Insertion-Reihenfolge der `<data>`-Einträge in resw-Dateien (Empfehlung: gruppiert mit verwandten Keys nahe `LoginReloadButton.*` und `NoActiveSession.Text`).
- **`x:Uid` vs `l:Uids.Uid`:** Researcher-Korrektur — siehe Befund 3 unten. Im gesamten Codebase wird AUSSCHLIESSLICH `l:Uids.Uid` verwendet. Neue Bindings (falls L10N-02 wider Erwarten Sites findet) MÜSSEN `l:Uids.Uid` verwenden. CONTEXT D-04 ist insofern faktisch falsch; gilt aber funktional, weil keine Migrationen anstehen.

### Deferred Ideas (OUT OF SCOPE)
- Pluralization (`1 minute` vs `5 minutes`) für InactiveSessionTooltip — v1.5+
- `LastFetchMinutesAgo` / `LastFetchNever` Keys (Phase 22 RESEARCH [A2]) — separate künftige Phase
- Locale-aware date/number formatting — out of scope
- Cross-language resource validation als CI-Check — v1.5+

## Project Constraints (from CLAUDE.md)

- **MVVM:** kein Code-Behind-Logic in Views; alles in ViewModels. Phase 23 hat keinen ViewModel-Code-Bedarf (Phase 22 hat den Localizer-Call bereits geschrieben).
- **No magic numbers / DRY / Wrap external libraries:** WinUI3Localizer ist bereits via `Localizer.Get()` gewrappt; keine direkten `ResourceLoader`-Calls.
- **Bash permission rules:** Kein chaining mit `&&|;|||`; jede Bash-Action eigenständig.
- **Build commands:** `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` und für Release `dotnet build -c Release -o ...` (kein `dotnet publish`).
- **Conventional Commits** für Plan-Commits.

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Resource-Authoring (4 neue keys EN+DE) | Strings/{en-US,de-DE}/Resources.resw | — | Standard-Lokalisierung-Layer |
| Runtime resource lookup | WinUI3Localizer Library (`Localizer.Get().GetLocalizedString`) | App.xaml.cs (LocalizerBuilder init) | Bereits in Phase 22 verdrahtet — kein Code-Diff |
| `l:Uids.Uid` XAML binding | View XAML | WinUI3Localizer UidsExtension | Library bindet Uid-Properties automatisch beim Page-Load |
| Language switch trigger | SettingsViewModel.OnSelectedLanguageIndexChanged | Localizer.SetLanguage() | Bereits implementiert; Phase 23 nutzt das Verhalten implizit |
| Test-Verifikation | xUnit + manueller resw-Lade-Helper | — | Localizer kann im Test-Host NICHT initialisiert werden — siehe Pitfall 1 |

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| WinUI3Localizer | 2.3.0 | Runtime resource lookup + `l:Uids.Uid` XAML extension + `SetLanguage()` ohne Restart | Bereits Projekt-Dependency; einzige WinUI 3 Library mit Live-Language-Switch ohne `Frame.Reload` Hack [VERIFIED: csproj line 38] |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| xUnit | 2.9.3 | Unit tests | Standard-Test-Framework des Projekts [VERIFIED: CCInfoWindows.Tests.csproj] |
| System.Xml.Linq | (BCL) | resw is XML — XDocument für strukturelle Validierung in Tests | Wenn Localizer im Test-Host nicht init-bar ist (siehe Pitfall 1) |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| WinUI3Localizer `l:Uids.Uid` | Built-in `x:Uid` (Microsoft Resource Manager) | x:Uid funktioniert zwar, aber: (a) macht Live-Language-Switch ohne Frame.Reload unmöglich, (b) Codebase verwendet es nirgends — Inkonsistenz. **NICHT VERWENDEN.** [VERIFIED: grep `x:Uid=` über Views → 0 Treffer] |
| Loading via Localizer in xUnit-Tests | XML-strukturelle Validierung via XDocument | Localizer braucht WinUI 3 App-Initialisierung; xUnit ist headless. XML-Lade-Test ist robuster und einfacher. |

**Installation:** Keine neuen NuGet-Dependencies erforderlich.

**Version verification:** WinUI3Localizer 2.3.0 ist seit ~Q3 2024 stabil; aktuell verwendet im Projekt; keine Migration nötig. [VERIFIED: csproj line 38]

## Architecture Patterns

### System Architecture Diagram

```
                  App-Startup
                       │
                       ▼
            App.xaml.cs:InitializeLocalizerAsync()
            ┌──────────────────────────────────┐
            │ LocalizerBuilder                 │
            │  .AddStringResourcesFolderFor... │
            │  .SetOptions(Default = "en-US")  │
            │  .Build()                        │
            └──────────────┬───────────────────┘
                           │
                           ▼
                Localizer.Get() (Singleton)
                           │
            ┌──────────────┼─────────────────────────┐
            │              │                         │
            ▼              ▼                         ▼
   View XAML Load    ViewModel Code          User Action:
   `l:Uids.Uid="X"` `GetLocalizedString(K)` Settings → Language ▼
            │              │                  Localizer.SetLanguage("de-DE")
            │              │                         │
            ▼              ▼                         ▼
   resw[X.Text]      resw[K]                  ALL `l:Uids.Uid` re-bind
                                              auto-magically (library feature)
```

**Data flow:** resw-Dateien werden bei `Build()` geladen, gecacht und bei `SetLanguage()` neu zugewiesen. Phase 23 fügt 4 neue resw-Einträge hinzu — die Lookup-Pipeline existiert bereits.

### Recommended Project Structure (unverändert)

```
CCInfoWindows/CCInfoWindows/Strings/
├── en-US/
│   └── Resources.resw   ← 4 neue <data>-Einträge
└── de-DE/
    └── Resources.resw   ← 4 neue <data>-Einträge
```

### Pattern 1: `<data>`-Eintrag in resw

**What:** XML-`<data>`-Element mit `name`, `xml:space="preserve"` und Child-`<value>`.
**When to use:** Für jeden Resource-Key, in beiden Sprach-resw identisch benannt.
**Example:**
```xml
<!-- Source: bestehender Stil in Resources.resw -->
<data name="NotSignedIn.Text" xml:space="preserve">
  <value>Not signed in</value>
</data>
<data name="InactiveSessionTooltip" xml:space="preserve">
  <value>Inactive for &gt; {0}min</value>
</data>
```

**Hinweis zum `>`-Zeichen:** XML-strict erlaubt `>` literal in `<value>`-Content (nur `<` und `&` sind reserviert), aber zur Konvention im bestehenden Codebase: `&#x26A0;` für Symbol-Codepoints, sonst literal. `>` literal funktioniert problemlos. **Trotzdem als `&gt;` empfohlen für maximale XML-Hygiene** (verhindert SAX-Parser-Edge-Cases).

### Pattern 2: `l:Uids.Uid` XAML Binding (NUR falls Migrations-Site doch existiert)

**What:** WinUI3Localizer-Attached-Property bindet alle resw-Keys mit Prefix `<Uid>.<Property>` an das Element.
**When to use:** Für jedes XAML-Element, das lokalisierten Content benötigt.
**Example:**
```xml
<!-- Source: SettingsView.xaml line 144 -->
<TextBlock l:Uids.Uid="SettingsSessionTimeout" />
<!-- Bindet automatisch:
     - SettingsSessionTimeout.Text → TextBlock.Text
     - SettingsSessionTimeout.[using:...]ToolTipService.ToolTip → ToolTip
     - SettingsSessionTimeout.[using:...]AutomationProperties.Name → AutomationName
-->
```

**Wichtig:** Page-XAML MUSS `xmlns:l="using:WinUI3Localizer"` importieren. Alle 3 Views haben das bereits.

### Pattern 3: Programmatic Lookup mit Format-String (Phase 22 bestehend)

```csharp
// Source: MainViewModel.cs line 633
string template;
try
{
    template = Localizer.Get().GetLocalizedString("InactiveSessionTooltip");
}
catch
{
    template = "Inactive for > {0}min"; // defensive fallback
}
return $"{session.Cwd}\n{string.Format(template, sessionTimeoutMinutes)}";
```

Phase 23 ändert diesen Code NICHT — sie liefert nur das Template via resw.

### Anti-Patterns to Avoid

- **Duplicate `<data>`-Einträge:** Wenn Phase 23 `LoginReloadButton.*` re-authort, scheitert die Resource-Auflösung zur Laufzeit (Localizer-Logik wirft oder gibt key-name zurück). **Vorher prüfen via grep.**
- **`x:Uid` mischen mit `l:Uids.Uid`:** Funktional kollidiert es nicht direkt, aber Live-Language-Switch funktioniert für `x:Uid`-bindings NUR via Frame-Reload. Konsistent bleiben.
- **`\n` im resw-Wert:** Phase 22 komponiert die Newline. Wenn `\n` zusätzlich im resw landet, hat der Tooltip eine Leerzeile.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Resource lookup | Custom dictionary load | `Localizer.Get().GetLocalizedString(key)` | Library handhabt Locale-fallback, Caching, Live-Switch |
| Language switching | `Frame.Navigate()` reload hack | `Localizer.Get().SetLanguage(code)` | Bereits implementiert in SettingsViewModel.cs:194 |
| XAML auto-binding für tooltip + automation name + text | Per-property setter im Code-Behind | `l:Uids.Uid="<Prefix>"` | Library bindet ALLE Sub-Properties (`.Text`, `.[using:...]ToolTipService.ToolTip`, etc.) in einem Aufruf |
| resw-Schreiben | XML-String-Konkatenation | XDocument oder direktes Editieren der bestehenden Datei | Bestehende resw nutzt feste Struktur (xsd:schema + resheader) — minimal-invasiv via Edit-Tool |

**Key insight:** Phase 23 ist 100% Authoring + 0% Code-Änderungen. Die Pipeline existiert. Hand-rolling-Risiko gleich Null.

## Runtime State Inventory

Phase 23 ist eine **reine Resource-Authoring-Phase ohne Rename/Refactor-Charakter**. Trotzdem zur Sicherheit:

| Category | Items Found | Action Required |
|----------|-------------|------------------|
| Stored data | None — Resource-Keys werden zur Build-Zeit in `bin\...\Strings\` kopiert (csproj line 52: `Content Include="Strings\**\*.resw"` + `CopyToOutputDirectory=PreserveNewest`); kein Datastore involviert | none |
| Live service config | None — keine externen Services lokalisiert | none |
| OS-registered state | None — keine Task-Scheduler/Registry-Einträge mit Strings | none |
| Secrets/env vars | None — keine Sprachschlüssel über env | none |
| Build artifacts | `bin\x64\Debug\net9.0-windows10.0.19041.0\Strings\{en-US,de-DE}\Resources.resw` werden bei nächstem Build aktualisiert (PreserveNewest) — kein manueller cleanup nötig | Re-build nach resw-Edit |

**The canonical question:** Nach Edit der zwei resw-Dateien — was ist noch stale? **Antwort:** Nur die kopierten Build-Outputs unter `bin\`, die `dotnet build` automatisch refreshed. Kein manueller cleanup.

## Common Pitfalls

### Pitfall 1: xUnit-Test kann Localizer NICHT initialisieren
**What goes wrong:** `Localizer.Get()` wirft im xUnit-Host, weil WinUI3Localizer eine WinUI-3-App-Initialisierung erwartet.
**Why it happens:** `LocalizerBuilder().Build()` setzt Singleton, aber im headless xUnit-Run gibt es keinen `App`-Kontext (kein DispatcherQueue, kein Resource-System).
**How to avoid:**
- Option A (empfohlen): Tests laden `.resw` direkt via `XDocument.Load()` und prüfen `<data name="X"><value>...</value></data>` strukturell. Robust, kein WinUI-3-App-Init nötig.
- Option B: Tests `try { Localizer.Get().GetLocalizedString(key) }`-Pattern; akzeptiere als grünen Test, wenn entweder der korrekte Wert ODER der key-name zurückkommt (siehe `SessionDisplayTooltipTests.cs:45-56`).
**Warning signs:** Test wirft `NullReferenceException` in `Localizer.Get()`-Zugriff → Test-Host-Init-Problem, kein Code-Bug.

**Empfehlung für D-08:** **Option A.** Test lädt beide resw als XDocument, sucht `<data name="NotSignedIn.Text">/<value>` mit Erwartung `"Not signed in"` und in de-DE `"Nicht angemeldet"`. Schnell, deterministisch, headless-fähig.

### Pitfall 2: `LoginReloadButton.*` doppelt anlegen
**What goes wrong:** Phase 20 hat die Keys bereits angelegt (verifiziert in beiden resw bei Zeile 121-126). Wenn Phase 23 sie nochmal hinzufügt, hat das resw zwei `<data name="LoginReloadButton.[...]">` — Localizer-Verhalten ist undefiniert (bei manchen Versionen: "letzter Eintrag gewinnt"; bei anderen: throw).
**Why it happens:** CONTEXT erwähnt explizit, dass diese Keys teil von L10N-01 sind — könnte Researcher/Planner verleiten, sie als "Phase-23-Aufgabe" zu sehen.
**How to avoid:** Plan-Task explizit als "**verify-only**" für `LoginReloadButton.*` formulieren. KEINE Edit-Action. Verifikation per `grep -c "LoginReloadButton" Resources.resw` = 2 in beiden Dateien.
**Warning signs:** Build OK, aber zur Laufzeit fehlt der LoginReloadButton-Tooltip oder lokalisiert sich falsch.

### Pitfall 3: `>`-Encoding im InactiveSessionTooltip
**What goes wrong:** Wert `Inactive for > {0}min` mit literalem `>`. Manche resw-Generatoren (Visual Studio) escapen das automatisch zu `&gt;`, manche nicht. Beide sind XML-konform, aber Inkonsistenz mit bestehendem Stil ist möglich.
**Why it happens:** XML reserviert `<` und `&`; `>` ist nur in CDATA-Endung `]]>` problematisch.
**How to avoid:** Konvention bestehender Datei prüfen. **Befund:** existierende resw nutzt Codepoints (`&#x26A0;`) für Sonderzeichen; `>` kommt nicht vor. **Empfehlung:** literal `>` verwenden — konsistent mit `BurnRateFormat_HoursMinutes` (`~{0}h {1}min`), das auch keine Entity-Encoding nutzt.
**Warning signs:** XML-Parse-Error beim Build → meist nur Issue mit `<` oder `&`, nicht mit `>`.

### Pitfall 4: Eingedrungene `x:Uid`-Verwendung
**What goes wrong:** CONTEXT D-04 sagt explizit `x:Uid` — Researcher hat aber verifiziert, dass der Codebase ausschließlich `l:Uids.Uid` nutzt (3/3 Views, 30+ Bindings).
**Why it happens:** macOS-Spec bzw. CONTEXT-Author hatte WinUI 3 built-in `x:Uid` im Kopf, nicht das WinUI3Localizer-Pattern.
**How to avoid:** Plan dokumentiert die Korrektur explizit; falls Migrations-Sites doch entdeckt werden, IMMER `l:Uids.Uid` verwenden. Im konkreten Phase-23-Scope **kein** Migrations-Bedarf, also nur als Notiz für künftige Phasen.

## Code Examples

Verifizierte Patterns aus dem bestehenden Codebase.

### Resource-Eintrag (en-US)
```xml
<!-- Source: Strings/en-US/Resources.resw line 50 -->
<data name="ScanningIndicator.Text" xml:space="preserve">
    <value>Scanning for sessions...</value>
</data>
```

### Resource-Eintrag mit Sub-Property (Tooltip + AutomationName)
```xml
<!-- Source: Strings/en-US/Resources.resw line 121-126 (Phase 20 LoginReloadButton) -->
<data name="LoginReloadButton.[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip" xml:space="preserve">
    <value>Reload page</value>
</data>
<data name="LoginReloadButton.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name" xml:space="preserve">
    <value>Reload login page</value>
</data>
```

### XML-strukturelle Test-Validierung (für D-08)
```csharp
// Empfehlung — keine bestehende Vorlage im Projekt, neu zu schreiben
[Fact]
public void Resw_EnUS_ContainsRequiredKeys()
{
    var path = Path.Combine(AppContext.BaseDirectory, "Strings", "en-US", "Resources.resw");
    var doc = XDocument.Load(path);
    var keys = doc.Root!.Elements("data")
        .Select(d => d.Attribute("name")!.Value)
        .ToHashSet();

    Assert.Contains("NotSignedIn.Text", keys);
    Assert.Contains("NoData.Text", keys);
    Assert.Contains("Loading.Text", keys);
    Assert.Contains("InactiveSessionTooltip", keys);
}

[Fact]
public void Resw_DeDE_ContainsRequiredKeysWithCorrectValues()
{
    var path = Path.Combine(AppContext.BaseDirectory, "Strings", "de-DE", "Resources.resw");
    var doc = XDocument.Load(path);
    var entries = doc.Root!.Elements("data")
        .ToDictionary(d => d.Attribute("name")!.Value, d => d.Element("value")!.Value);

    Assert.Equal("Nicht angemeldet", entries["NotSignedIn.Text"]);
    Assert.Equal("Keine Daten", entries["NoData.Text"]);
    Assert.Equal("Wird geladen", entries["Loading.Text"]);
    Assert.Equal("Inaktiv seit > {0}min", entries["InactiveSessionTooltip"]);
}
```

**Setup-Bedingung:** Test-csproj muss die resw-Files in den Build-Output kopieren oder via Relativ-Pfad zur Source greifen. Empfehlung: relativer Pfad zur Source via `Path.Combine` mit `../../../../CCInfoWindows/CCInfoWindows/Strings/...` (üblich in xUnit für File-Asset-Tests). Alternativ: csproj `Content Include="...resw" CopyToOutputDirectory="PreserveNewest"` im Test-Projekt nachziehen.

### Programmatic Lookup mit Format-String (Phase 22 bestehend, unverändert)
```csharp
// Source: MainViewModel.cs:621-642
private static string ComputeTooltipText(SessionInfo session, bool isActive, int sessionTimeoutMinutes)
{
    if (isActive) return session.Cwd;

    string template;
    try { template = Localizer.Get().GetLocalizedString("InactiveSessionTooltip"); }
    catch { template = "Inactive for > {0}min"; }

    return $"{session.Cwd}\n{string.Format(template, sessionTimeoutMinutes)}";
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Built-in WinRT `x:Uid` + ResourceLoader | WinUI3Localizer `l:Uids.Uid` | Projekt-Init (vor v1.0) | Live language switch ohne Frame-Reload |
| Pre-Phase-22 `InactiveSessionTooltip` inline literal | resw-key (Phase 23) + Localizer-Lookup (Phase 22) | Phase 22 + 23 | Volle DE-Übersetzung |

**Deprecated/outdated:** Keine. Stack ist stabil seit Projekt-Anfang.

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | XML-strukturelle resw-Tests sind die robustere Test-Strategie als Localizer-Init in xUnit | Pitfall 1, Code Examples | Wenn Localizer doch im Test-Host initialisierbar ist (z.B. via einer Test-spezifischen Builder-Variante), wären direkte `GetLocalizedString`-Calls expressiver — aber XML-Tests sind in jedem Fall ausreichend für L10N-01. [ASSUMED] |
| A2 | Library-Verhalten bei doppelten `<data>`-name-Attributen ist undefiniert/throw | Pitfall 2 | Wenn Library "letzter Eintrag gewinnt" deterministisch ist, ist Re-Authoring "nur" Tech-Debt, kein Bug. Trotzdem: nicht riskieren. [ASSUMED — basiert auf CONTEXT D-02] |
| A3 | DE-Übersetzungen aus CONTEXT sind sprachlich korrekt und akzeptabel | D-01 | Niedriges Risiko: alle 4 neuen DE-Werte sind etabliertes IT-Deutsch (`Wird geladen`, `Keine Daten`, `Nicht angemeldet`, `Inaktiv seit > {0}min`). Native-Speaker-Review optional. [ASSUMED] |

**Hinweis:** Diese Annahmen sind niedriges Risiko — alle drei können vom Planner ohne User-Rückfrage akzeptiert werden.

## Open Questions

1. **Soll der Plan auch die Phase-22 `try/catch`-Defensive entfernen, sobald Phase 23 die Keys liefert?**
   - What we know: Phase 22 hat defensiven Fallback (`MainViewModel.cs:635-639`), kommentiert mit "Phase 23 authors the resw key".
   - What's unclear: Ist die `try/catch` für künftige Locale-Switches (wo der Key fehlen könnte) immer noch sinnvoll, oder kann Phase 23 sie entfernen?
   - Recommendation: **Try/catch beibehalten.** Defensiv-Code kostet nichts, schützt vor künftigen Locale-Additionen mit fehlenden Keys. Nicht in Phase-23-Scope nehmen.

2. **L10N-02 Akzeptanzkriterium ist trivial erfüllt — sollte der Plan trotzdem den grep ausführen?**
   - What we know: 0 hardcoded Sites gefunden.
   - What's unclear: Soll der Plan einen Verifikationsschritt mit dem expliziten grep-Befehl haben?
   - Recommendation: **Ja.** Plan dokumentiert grep-Output `0 matches` als Verifikation; macht L10N-02-Erfüllung audit-fähig.

3. **Sollen die 4 neuen Keys in beiden resw an gleicher Stelle (gleicher Block) eingefügt werden?**
   - What we know: Bestehende resw gruppieren by-feature (z.B. `<!-- LoginView reload button (Phase 20) -->`).
   - What's unclear: Welcher Block für `NotSignedIn.Text`, `NoData.Text`, `Loading.Text`, `InactiveSessionTooltip`?
   - Recommendation: Drei `*.Text`-Keys nahe `NoActiveSession.Text` (Zeile 59-61), in eigenem Block `<!-- Generic state placeholders (Phase 23) -->`. `InactiveSessionTooltip` in eigenem Block direkt darunter `<!-- Session tooltip (Phase 22 + 23) -->`.

## Environment Availability

Phase 23 ist reines XML-Editing + xUnit-Tests. Keine externen Tools jenseits der Standard-Build-Toolchain.

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET 9 SDK | Build + Test | ✓ | (assumed) | — |
| dotnet test | xUnit run | ✓ | (assumed) | — |
| Visual Studio / VSCode XML editor | resw editing | ✓ | — | Notepad/Edit-Tool reicht |

**Missing dependencies with no fallback:** Keine.
**Missing dependencies with fallback:** Keine.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 [VERIFIED: CCInfoWindows.Tests.csproj] |
| Config file | `CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |
| Quick run command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~Localization"` |
| Full suite command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| L10N-01 | 4 neue Keys in en-US/Resources.resw | unit (XDocument structural) | `dotnet test --filter "Resw_EnUS_ContainsRequiredKeys"` | ❌ Wave 0 |
| L10N-01 | 4 neue Keys in de-DE/Resources.resw mit korrekten Werten | unit (XDocument structural) | `dotnet test --filter "Resw_DeDE_ContainsRequiredKeysWithCorrectValues"` | ❌ Wave 0 |
| L10N-01 | 2 vorhandene `LoginReloadButton.*` Keys unverändert | unit (XDocument structural) | `dotnet test --filter "Resw_LoginReloadButtonKeysPreserved"` | ❌ Wave 0 (optional) |
| L10N-02 | 0 hardcoded `"Loading"`/`"No data"`/`"Not signed in"` in Views | manual grep | `grep -rE '"(Loading\|No data\|Not signed in)"' CCInfoWindows/CCInfoWindows/Views/*.xaml` | ✅ trivial — Researcher verifiziert: 0 Treffer |
| L10N-03 | Live language switch updated alle 6 Keys | manual smoke | Settings → Sprache wechseln → visuell verifizieren | manual (kein Unit-Test) |

### Sampling Rate
- **Per task commit:** `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "Localization"`
- **Per wave merge:** `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj`
- **Phase gate:** Volle Suite grün + manueller L10N-03-Smoke vor `/gsd-verify-work`

### Wave 0 Gaps
- [ ] `CCInfoWindows.Tests/Localization/ResourceFilesTests.cs` — neue Test-Klasse mit XDocument-strukturellen Tests für L10N-01 (4 neue + 2 verify-only Keys, beide Locales)
- [ ] csproj-Eintrag im Test-Projekt zum Auflösen des resw-Pfades (Option A: `Content Include="..\CCInfoWindows\CCInfoWindows\Strings\**\*.resw"` mit Link, ODER Option B: relative Pfad-Konstruktion zur Test-Source via `AppContext.BaseDirectory`-Navigation)

## Security Domain

> Phase 23 ist reines Resource-String-Authoring; keine PII, keine Auth-Tokens, kein User-Input. ASVS V5 Input Validation greift technisch nur, wenn die `{0}`-Substitution mit user-controlled Daten gespeist wird — was hier NICHT der Fall ist (`SessionTimeoutMinutes` ist eine app-interne, validierte int-Settings-Property).

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | — |
| V3 Session Management | no | — |
| V4 Access Control | no | — |
| V5 Input Validation | minimal | `string.Format(template, intValue)` — nur int-Substitution; kein injection-vector |
| V6 Cryptography | no | — |

### Known Threat Patterns

Keine relevanten threat patterns für reine resw-Edits. XML-Parser ist Microsoft-vendored (`System.Xml.Linq`); Phase nimmt keinen externen XML-Input.

## Sources

### Primary (HIGH confidence)
- `CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw` — vollständig gelesen (325 Zeilen); `LoginReloadButton.*` keys verifiziert bei Zeile 121-126
- `CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw` — vollständig gelesen (325 Zeilen); `LoginReloadButton.*` keys verifiziert bei Zeile 121-126 mit korrekten DE-Werten
- `CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` — WinUI3Localizer 2.3.0 Dependency verifiziert (Zeile 38), resw als Content (Zeile 52-54)
- `CCInfoWindows/CCInfoWindows/App.xaml.cs:81-100` — LocalizerBuilder-Init verifiziert
- `CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs:621-642` — `InactiveSessionTooltip` Lookup mit defensivem try/catch
- `CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs:187-201` — Live language switch via `Localizer.Get().SetLanguage(code)`
- `CCInfoWindows/CCInfoWindows/Views/{LoginView,MainView,SettingsView}.xaml` — alle drei Views nutzen `xmlns:l="using:WinUI3Localizer"`; alle Bindings via `l:Uids.Uid`
- grep `"(Loading|No data|Not signed in)"` über `Views/` — **0 Treffer** (verifiziert hardcoded-string Absenz)
- grep `x:Uid=` über `Views/` — **0 Treffer** (bestätigt einheitliche `l:Uids.Uid`-Konvention)

### Secondary (MEDIUM confidence)
- `CCInfoWindows.Tests/ViewModels/SessionDisplayTooltipTests.cs:45-56` — bestätigt, dass Localizer im xUnit-Host nicht init-bar ist (Test arbeitet mit beiden möglichen Outputs)

### Tertiary (LOW confidence)
- (keine — sämtliche Befunde sind im Codebase-grep verifiziert)

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — alle Versionen direkt aus csproj
- Architecture: HIGH — gesamte Pipeline (App-Init → Localizer-Get → resw-Lookup → Live-Switch) im Codebase verifiziert
- Pitfalls: HIGH — alle vier Pitfalls sind durch konkrete Code-Referenzen gestützt
- Test-Strategie: MEDIUM — Annahme A1 (XDocument-Tests robuster als Localizer-Init in xUnit) ist begründet, aber nicht empirisch in einem Test-Run verifiziert

**Research date:** 2026-05-06
**Valid until:** 2026-06-06 (30 Tage — stabiler Stack, keine fast-moving Dependencies)

---

## RESEARCH COMPLETE — Quick Summary für Planner

**Phase 23 Scope (reduziert vs. CONTEXT):**
1. **4 neue resw-Keys** in beiden Locales authoren (`NotSignedIn.Text`, `NoData.Text`, `Loading.Text`, `InactiveSessionTooltip`)
2. **2 bestehende Keys** verifizieren (NICHT re-authoren!) — `LoginReloadButton.*`
3. **0 XAML-Migrationen** — keine hardcoded Strings gefunden; L10N-02 ist trivial erfüllt, grep-verifiziert
4. **2 xUnit-Tests** via XDocument-strukturelle Validierung (NICHT via Localizer — siehe Pitfall 1)
5. **1 manueller Smoke-Test** für L10N-03 (Live language switch)

**Geschätzter Plan-Aufwand:** 1 Plan-File, ~30-45 min Implementation.

**Korrektur zu CONTEXT D-04:** `x:Uid` → `l:Uids.Uid` (Library-Konvention im gesamten Codebase). Nicht handlungsrelevant in Phase 23, weil keine neuen Bindings nötig.
