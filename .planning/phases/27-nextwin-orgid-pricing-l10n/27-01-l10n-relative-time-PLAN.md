---
phase: 27-nextwin-orgid-pricing-l10n
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs
  - CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw
  - CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw
  - CCInfoWindows.Tests/Localization/ResourceCoverageTests.cs
autonomous: true
requirements:
  - L10N-01
  - L10N-02
  - L10N-03

must_haves:
  truths:
    - "About-tab 'last fetched X minutes ago' label shows German text when CurrentUICulture is de-DE"
    - "About-tab 'last fetched X minutes ago' label shows English text when CurrentUICulture is en-US"
    - "ResourceCoverageTests fails the build when any of the 5 LastFetchRelative keys are missing in either locale"
    - "No hardcoded English literals 'Never', '1 minute ago', '{n} minutes ago' remain in SettingsViewModel.cs"
  artifacts:
    - path: "CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs"
      provides: "Localized LastFetchRelativeTime getter using Localizer.Get().GetLocalizedString(...) per category"
      contains: "LastFetchRelative.JustNow"
    - path: "CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw"
      provides: "5 LastFetchRelative.* DE keys with format placeholders"
      contains: "LastFetchRelative.MinutesAgo"
    - path: "CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw"
      provides: "5 LastFetchRelative.* EN keys with format placeholders"
      contains: "LastFetchRelative.MinutesAgo"
    - path: "CCInfoWindows.Tests/Localization/ResourceCoverageTests.cs"
      provides: "Extended structural validation covering Phase 27 keys"
      contains: "LastFetchRelative"
  key_links:
    - from: "SettingsViewModel.LastFetchRelativeTime"
      to: "Localizer.Get().GetLocalizedString(\"LastFetchRelative.*\")"
      via: "5 categorical branches (Never / JustNow / MinutesAgo / HoursAgo / DaysAgo)"
      pattern: "GetLocalizedString\\(\"LastFetchRelative\\."
    - from: "ResourceCoverageTests"
      to: "5 new LastFetchRelative keys + glob coverage of MainView.NextWindow.* / MainView.PricingErrorInfoBar.* / MainView.OrgMismatchInfoBar.* / Settings.Account.Redetect* / Dialog.OrgPicker.* (defensive forward-coverage for plans 27-02..27-04)"
      via: "RequiredKeys list extension"
      pattern: "LastFetchRelative\\.(JustNow|MinutesAgo|HoursAgo|DaysAgo|Never)"
---

<objective>
Replace the hardcoded English `LastFetchRelativeTime` getter in `SettingsViewModel.cs` with a fully
localized 5-category implementation backed by 5 new resw key pairs (`LastFetchRelative.JustNow`,
`.MinutesAgo`, `.HoursAgo`, `.DaysAgo`, `.Never`) in `de-DE` + `en-US`. Extend
`ResourceCoverageTests` to cover the new keys structurally.

This is **Wave 1** because it touches `SettingsViewModel.cs` (independent of `MainViewModel.cs`) and
the `.resw` files. All later plans in this phase (27-02 NEXTWIN, 27-03 PRICING, 27-04 ORGID) also
write to `Resources.resw`, so this plan ships first to seed the file structure with the
forward-coverage comment block.

Purpose: M-2 v1.4 code-review remediation (`LastFetchRelativeTime` hardcoded EN strings) +
project-wide L10N hygiene + test gate for all v1.5 keys.

Output:
- 5 new resw key pairs in DE + EN (10 entries total)
- Refactored `LastFetchRelativeTime` getter (no English literals)
- Extended `ResourceCoverageTests` covering 5 new keys
- Forward-coverage comment block listing planned Phase 27 keys (27-02..27-04 will append, not replace)
</objective>

<execution_context>
@$HOME/.claude/get-shit-done/workflows/execute-plan.md
@$HOME/.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/ROADMAP.md
@.planning/STATE.md
@.planning/phases/27-nextwin-orgid-pricing-l10n/27-CONTEXT.md
@.planning/phases/26-persistent-session-renaming/26-03-settings-sessions-tab-PLAN.md

@CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs
@CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw
@CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw
@CCInfoWindows.Tests/Localization/ResourceCoverageTests.cs

<interfaces>
<!-- Existing Localizer.Get() pattern (used widely; no changes needed) -->

WinUI3Localizer pattern:
```csharp
using WinUI3Localizer;
// Static call returns localized string for the active CurrentUICulture
string label = Localizer.Get().GetLocalizedString("LastFetchRelative.JustNow");
// For format-placeholder keys:
string text = string.Format(
    Localizer.Get().GetLocalizedString("LastFetchRelative.MinutesAgo"),
    minutes);
```

Existing SettingsViewModel.LastFetchRelativeTime (lines 133-145):
```csharp
public string LastFetchRelativeTime
{
    get
    {
        var lastFetch = _pricingService.LastFetch;
        if (!lastFetch.HasValue)
            return "Never";

        var elapsed = DateTimeOffset.Now - lastFetch.Value;
        var minutes = (int)Math.Max(0, elapsed.TotalMinutes);
        return minutes == 1 ? "1 minute ago" : $"{minutes} minutes ago";
    }
}
```

Existing ResourceCoverageTests structure (CCInfoWindows.Tests/Localization/ResourceCoverageTests.cs):
- `RequiredKeys` array — extend with 5 new keys
- `ExpectedEnUs` dictionary — add 5 EN string-format values
- `ExpectedDeDe` dictionary — add 5 DE string-format values
- Uses `XDocument.Load(...)` to parse resw and assert `<data name="..." xml:space="preserve"><value>...</value></data>` entries
</interfaces>
</context>

<tasks>

<task type="auto" tdd="true">
  <name>Task 1: Add 5 LastFetchRelative.* resw key pairs to de-DE and en-US</name>
  <files>
    CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw,
    CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw
  </files>
  <behavior>
    - en-US contains `LastFetchRelative.JustNow` = "just now"
    - en-US contains `LastFetchRelative.MinutesAgo` = "{0} minutes ago"
    - en-US contains `LastFetchRelative.HoursAgo` = "{0} hours ago"
    - en-US contains `LastFetchRelative.DaysAgo` = "{0} days ago"
    - en-US contains `LastFetchRelative.Never` = "Never"
    - de-DE contains `LastFetchRelative.JustNow` = "gerade eben"
    - de-DE contains `LastFetchRelative.MinutesAgo` = "vor {0} Minuten"
    - de-DE contains `LastFetchRelative.HoursAgo` = "vor {0} Stunden"
    - de-DE contains `LastFetchRelative.DaysAgo` = "vor {0} Tagen"
    - de-DE contains `LastFetchRelative.Never` = "Nie"
  </behavior>
  <action>
Per **D-L10-01**: add 5 string keys to BOTH locale resw files. The `.MinutesAgo`, `.HoursAgo`,
`.DaysAgo` keys use `{0}` `string.Format` placeholders. `.JustNow` and `.Never` are static.

**Exact resw entries to insert** (append at end of each file before the closing `</root>` tag,
mirroring the existing `<data name="...">` block style — preserve `xml:space="preserve"` attribute):

en-US/Resources.resw:
```xml
<data name="LastFetchRelative.JustNow" xml:space="preserve">
  <value>just now</value>
</data>
<data name="LastFetchRelative.MinutesAgo" xml:space="preserve">
  <value>{0} minutes ago</value>
</data>
<data name="LastFetchRelative.HoursAgo" xml:space="preserve">
  <value>{0} hours ago</value>
</data>
<data name="LastFetchRelative.DaysAgo" xml:space="preserve">
  <value>{0} days ago</value>
</data>
<data name="LastFetchRelative.Never" xml:space="preserve">
  <value>Never</value>
</data>
```

de-DE/Resources.resw:
```xml
<data name="LastFetchRelative.JustNow" xml:space="preserve">
  <value>gerade eben</value>
</data>
<data name="LastFetchRelative.MinutesAgo" xml:space="preserve">
  <value>vor {0} Minuten</value>
</data>
<data name="LastFetchRelative.HoursAgo" xml:space="preserve">
  <value>vor {0} Stunden</value>
</data>
<data name="LastFetchRelative.DaysAgo" xml:space="preserve">
  <value>vor {0} Tagen</value>
</data>
<data name="LastFetchRelative.Never" xml:space="preserve">
  <value>Nie</value>
</data>
```

DO NOT use plural-forms (zero/one/few) — the existing project does not use `.lang.json` plural
schemas. Linguistic singular-form imperfection ("vor 1 Minuten") is **explicitly accepted** per
project history (mirrors `InactiveSessionTooltip` Phase 23 precedent).

DO NOT add `<comment>` elements — the existing resw entries do not use comments.
  </action>
  <verify>
    <automated>node -e "const {XMLParser}=require('fast-xml-parser');const fs=require('fs');const en=fs.readFileSync('D:/myProjects/ccInfoWin/CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw','utf8');const de=fs.readFileSync('D:/myProjects/ccInfoWin/CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw','utf8');const keys=['LastFetchRelative.JustNow','LastFetchRelative.MinutesAgo','LastFetchRelative.HoursAgo','LastFetchRelative.DaysAgo','LastFetchRelative.Never'];const missing=keys.filter(k=>!en.includes('name=\"'+k+'\"')||!de.includes('name=\"'+k+'\"'));if(missing.length){console.error('Missing:',missing);process.exit(1);}console.log('OK: all 5 keys present in both locales');" 2>&1 || (powershell -Command "$keys=@('LastFetchRelative.JustNow','LastFetchRelative.MinutesAgo','LastFetchRelative.HoursAgo','LastFetchRelative.DaysAgo','LastFetchRelative.Never');$en=Get-Content 'D:/myProjects/ccInfoWin/CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw' -Raw;$de=Get-Content 'D:/myProjects/ccInfoWin/CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw' -Raw;$miss=@();foreach($k in $keys){if($en -notmatch [regex]::Escape('name=\"'+$k+'\"')){$miss+='en:'+$k};if($de -notmatch [regex]::Escape('name=\"'+$k+'\"')){$miss+='de:'+$k}};if($miss.Count -gt 0){Write-Error ('Missing: '+($miss -join ', '));exit 1};Write-Output 'OK'")</automated>
  </verify>
  <done>Both resw files contain all 5 `LastFetchRelative.*` keys with the exact strings above. XML structure remains valid (file parseable as XDocument).</done>
</task>

<task type="auto" tdd="true">
  <name>Task 2: Refactor SettingsViewModel.LastFetchRelativeTime to use Localizer per-category</name>
  <files>CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs</files>
  <behavior>
    - Returns `LastFetchRelative.Never` localized string when `_pricingService.LastFetch` is null
    - Returns `LastFetchRelative.JustNow` localized string when elapsed < 30 seconds
    - Returns `string.Format(LastFetchRelative.MinutesAgo, n)` when 30s <= elapsed < 60min
    - Returns `string.Format(LastFetchRelative.HoursAgo, n)` when 60min <= elapsed < 24h
    - Returns `string.Format(LastFetchRelative.DaysAgo, n)` when elapsed >= 24h
    - No hardcoded English string literals "Never", "minute", "ago" remain
  </behavior>
  <action>
Per **D-L10-02** + **specifics block sketch**: replace the existing getter at
`SettingsViewModel.cs:133-145` with the 5-branch implementation. Use `DateTimeOffset.Now`
(consistent with line 141; do NOT switch to `UtcNow` — the existing `_pricingService.LastFetch` is
local). Use `Math.Max(0, ...)` floor on elapsed deltas to defend against clock-skew negative values.

**Exact replacement** (replace lines 127-145, preserve XML doc comment style but update wording):

```csharp
/// <summary>
/// Localized "X minutes ago" string for the About tab. Re-evaluated on each
/// _aboutTimestampTimer Tick (D-09, D-11). L10N-01: 5 categories backed by
/// LastFetchRelative.* resw keys; switches DE/EN via CurrentUICulture.
/// </summary>
public string LastFetchRelativeTime
{
    get
    {
        var lastFetch = _pricingService.LastFetch;
        if (!lastFetch.HasValue)
            return Localizer.Get().GetLocalizedString("LastFetchRelative.Never");

        var elapsed = DateTimeOffset.Now - lastFetch.Value;
        if (elapsed.TotalSeconds < 30)
            return Localizer.Get().GetLocalizedString("LastFetchRelative.JustNow");

        if (elapsed.TotalMinutes < 60)
        {
            var minutes = (int)Math.Max(0, elapsed.TotalMinutes);
            return string.Format(
                Localizer.Get().GetLocalizedString("LastFetchRelative.MinutesAgo"),
                minutes);
        }

        if (elapsed.TotalHours < 24)
        {
            var hours = (int)Math.Max(0, elapsed.TotalHours);
            return string.Format(
                Localizer.Get().GetLocalizedString("LastFetchRelative.HoursAgo"),
                hours);
        }

        var days = (int)Math.Max(0, elapsed.TotalDays);
        return string.Format(
            Localizer.Get().GetLocalizedString("LastFetchRelative.DaysAgo"),
            days);
    }
}
```

**Critical:** preserve the existing `using WinUI3Localizer;` directive at line 11 (already present).
DO NOT add a new property; replace the existing one. The `OnPropertyChanged(nameof(LastFetchRelativeTime))`
calls at lines 468 and 489 (timer tick + LastFetch update) MUST remain unchanged — they trigger the
re-evaluation with localized output.

**No XAML changes required** — `LastFetchRelativeTime` is bound from `SettingsView.xaml` already
(verified via grep — search for `LastFetchRelativeTime` in `SettingsView.xaml` to confirm; if
binding uses `x:Bind ... Mode=OneWay` it auto-refreshes on `OnPropertyChanged`).
  </action>
  <verify>
    <automated>powershell -Command "$src=Get-Content 'D:/myProjects/ccInfoWin/CCInfoWindows/CCInfoWindows/ViewModels/SettingsViewModel.cs' -Raw;$bad=@('\"Never\"','\"just now\"','\"1 minute ago\"','\" minutes ago\"','\" hour ago\"');$found=@();foreach($needle in $bad){if($src -match [regex]::Escape($needle)){$found+=$needle}};if($found.Count -gt 0){Write-Error ('Hardcoded literals remain: '+($found -join ', '));exit 1};if(-not ($src -match 'LastFetchRelative\\.JustNow')){Write-Error 'Missing JustNow key call';exit 1};if(-not ($src -match 'LastFetchRelative\\.MinutesAgo')){Write-Error 'Missing MinutesAgo key call';exit 1};Write-Output 'OK'"</automated>
  </verify>
  <done>The getter contains 5 distinct `Localizer.Get().GetLocalizedString("LastFetchRelative.*")` calls; no hardcoded English literals remain; build still compiles.</done>
</task>

<task type="auto" tdd="true">
  <name>Task 3: Extend ResourceCoverageTests with the 5 LastFetchRelative keys + Phase 27 forward-coverage</name>
  <files>CCInfoWindows.Tests/Localization/ResourceCoverageTests.cs</files>
  <behavior>
    - `RequiredKeys` array contains all 5 `LastFetchRelative.*` keys
    - `ExpectedEnUs` and `ExpectedDeDe` dictionaries contain matching string values for the 5 new keys
    - Tests pass against the resw files modified in Task 1
    - When `dotnet test` is run, `ResourceCoverageTests` reports zero failures
  </behavior>
  <action>
Per **D-L10-03** + **CD-05**: extend the static fields. **Use the explicit-list approach** (NOT the
glob pattern), because the existing test file uses explicit lists with `Expected*` dictionaries —
maintain the convention. Plans 27-02, 27-03, 27-04 will append their own keys to these arrays.

**Append to `RequiredKeys` array** (after the last existing entry, before the closing `]`):

```csharp
// Phase 27 L10N-01: localized last-fetch relative time on About tab
"LastFetchRelative.JustNow",
"LastFetchRelative.MinutesAgo",
"LastFetchRelative.HoursAgo",
"LastFetchRelative.DaysAgo",
"LastFetchRelative.Never",
```

**Append to `ExpectedEnUs`** dictionary:
```csharp
["LastFetchRelative.JustNow"] = "just now",
["LastFetchRelative.MinutesAgo"] = "{0} minutes ago",
["LastFetchRelative.HoursAgo"] = "{0} hours ago",
["LastFetchRelative.DaysAgo"] = "{0} days ago",
["LastFetchRelative.Never"] = "Never",
```

**Append to `ExpectedDeDe`** dictionary:
```csharp
["LastFetchRelative.JustNow"] = "gerade eben",
["LastFetchRelative.MinutesAgo"] = "vor {0} Minuten",
["LastFetchRelative.HoursAgo"] = "vor {0} Stunden",
["LastFetchRelative.DaysAgo"] = "vor {0} Tagen",
["LastFetchRelative.Never"] = "Nie",
```

**Add a class-level XML comment** above the class declaration documenting the forward-coverage
expectation (so plans 27-02, 27-03, 27-04 know to extend this file):

```csharp
/// Phase 27 extension policy:
///   - Plan 27-02 (NEXTWIN) appends MainView.NextWindow.LabelDe / .LabelEn
///   - Plan 27-03 (PRICING) appends MainView.PricingErrorInfoBar.Title / .Message
///   - Plan 27-04 (ORGID) appends Settings.Account.RedetectButton + Dialog.OrgPicker.* + MainView.OrgMismatchInfoBar.*
```

Place the comment block under the existing `Phase 23 L10N-01 structural validation` summary, as a
trailing paragraph.

**No structural code changes** — this task purely extends the data tables. No new test methods
needed (existing parameterized tests over `RequiredKeys` will pick up new entries).
  </action>
  <verify>
    <automated>dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~ResourceCoverageTests" --nologo</automated>
  </verify>
  <done>`dotnet test` against `ResourceCoverageTests` passes (0 failures). All 5 LastFetchRelative keys are validated in both locales. The forward-coverage comment block is present.</done>
</task>

</tasks>

<threat_model>
## Trust Boundaries

| Boundary | Description |
|----------|-------------|
| user UI culture → Localizer | `CultureInfo.CurrentUICulture` is set by app at startup based on user OS preference; trusted internal-only path |
| resw file → string.Format | format keys (`{0}` placeholder) are loaded from app's bundled resources (read-only, signed assembly); trusted source |

## STRIDE Threat Register

| Threat ID | Category | Component | Disposition | Mitigation Plan |
|-----------|----------|-----------|-------------|-----------------|
| T-27-01-01 | Tampering | resw files at install location | accept | files are bundled in MSIX assembly, not user-editable; OS code-integrity protects on install |
| T-27-01-02 | Information Disclosure | LastFetch timestamp in localized string | accept | timestamp is local-time pricing-fetch, not sensitive PII |
| T-27-01-03 | Denial of Service | malformed format string crashes string.Format | mitigate | resw values are author-controlled; CI test (ResourceCoverageTests) verifies expected literal content; no user input flows into format string |
</threat_model>

<verification>
Run `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` — must succeed with no new errors/warnings.
Run `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~ResourceCoverageTests" --nologo` — all green.
Manual smoke (optional): launch app with `de-DE` culture → Settings → About → "Letzter Abruf" line shows German localized text ("Nie", "vor X Minuten").
</verification>

<success_criteria>
1. `LastFetchRelativeTime` returns localized string for all 5 categories in both DE and EN
2. `ResourceCoverageTests` passes covering all 5 new keys
3. No hardcoded English literals "Never" / "1 minute ago" / "minutes ago" remain in `SettingsViewModel.cs`
4. Build is green, no new test failures introduced
</success_criteria>

<output>
After completion, create `.planning/phases/27-nextwin-orgid-pricing-l10n/27-01-SUMMARY.md` documenting:
- 5 new resw key pairs added (DE + EN string values for each)
- Refactored getter (line range and approach)
- Test coverage delta
- Forward-coverage policy documented in test class
</output>
