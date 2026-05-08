---
phase: 27-nextwin-orgid-pricing-l10n
plan: 02
type: execute
wave: 2
depends_on:
  - 27-01
files_modified:
  - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
  - CCInfoWindows/CCInfoWindows/Views/MainView.xaml
  - CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw
  - CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw
  - CCInfoWindows.Tests/Localization/ResourceCoverageTests.cs
autonomous: true
requirements:
  - NEXTWIN-01
  - NEXTWIN-02
  - NEXTWIN-03

must_haves:
  truths:
    - "User sees an absolute reset-time TextBlock below the existing 5h-window countdown when ResetsAt is non-null"
    - "When ResetsAt is null OR IsSessionExpired is true, the absolute-time TextBlock is collapsed (NOT showing '—')"
    - "When CurrentUICulture is de-DE, the label uses format 'ddd d.M. HH:mm' (e.g. 'Mo 1.5. 16:30')"
    - "When CurrentUICulture is en-US, the label uses format 'ddd HH:mm' (e.g. 'Wed 14:30')"
    - "After the 5h window resets and a new ResetsAt arrives via API, the label updates without page reload"
  artifacts:
    - path: "CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs"
      provides: "FiveHourNextWindowText + IsFiveHourNextWindowVisible ObservableProperty + recompute helper"
      contains: "FiveHourNextWindowText"
    - path: "CCInfoWindows/CCInfoWindows/Views/MainView.xaml"
      provides: "TextBlock element below FiveHourCountdown bound to FiveHourNextWindowText"
      contains: "FiveHourNextWindowText"
    - path: "CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw"
      provides: "MainView.NextWindow.LabelDe DE format string"
      contains: "MainView.NextWindow.LabelDe"
    - path: "CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw"
      provides: "MainView.NextWindow.LabelEn EN format string"
      contains: "MainView.NextWindow.LabelEn"
  key_links:
    - from: "MainViewModel.UpdateUsagePropertiesAsync (line 504 area)"
      to: "RecomputeNextWindowLabel(_fiveHourResetsAt)"
      via: "called immediately after FiveHourCountdown is set"
      pattern: "RecomputeNextWindowLabel"
    - from: "MainViewModel partial OnIsSessionExpiredChanged"
      to: "RecomputeNextWindowLabel(_fiveHourResetsAt)"
      via: "ensures label hides when auth banner appears (D-NW-02)"
      pattern: "OnIsSessionExpiredChanged"
    - from: "MainView.xaml TextBlock"
      to: "ViewModel.FiveHourNextWindowText + ViewModel.IsFiveHourNextWindowVisible"
      via: "x:Bind OneWay + Visibility converter"
      pattern: "IsFiveHourNextWindowVisible"
---

<objective>
Add a second time label below the existing 5h-window countdown in MainView. The label shows the
absolute reset time of the current 5-hour window (e.g. "Mo 1.5. 16:30" in DE, "Wed 14:30" in EN),
sourced from `UsageResponse.FiveHour.ResetsAt`. The label is hidden — not "—" — when `ResetsAt` is
null or when the auth banner is showing.

Wave 2 (after 27-01) because:
- `Resources.resw` was last modified by 27-01; serializing avoids merge-conflict on resw header
- 27-02 also needs `ResourceCoverageTests` extended (already opened by 27-01)
- This plan does NOT touch `PollUsageCoreAsync` or `_pricingService` (no overlap with 27-03)

Purpose: NEXTWIN-01..03 — macOS v1.12.0 feature parity (next 5h-window start label).

Output:
- 2 new resw key pairs (`MainView.NextWindow.LabelDe` and `.LabelEn` — format strings)
- 2 new ObservableProperty fields in `MainViewModel` (`FiveHourNextWindowText`, `IsFiveHourNextWindowVisible`)
- Recompute helper called from 5h-window update sites + `IsSessionExpired` partial
- New TextBlock in `MainView.xaml` directly below the existing FiveHourCountdown TextBlock
- `ResourceCoverageTests` extended with 2 new keys
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
@.planning/phases/27-nextwin-orgid-pricing-l10n/27-01-l10n-relative-time-PLAN.md

@CCInfoWindows/CCInfoWindows/Models/UsageResponse.cs
@CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
@CCInfoWindows/CCInfoWindows/Views/MainView.xaml
@CCInfoWindows.Tests/Localization/ResourceCoverageTests.cs

<interfaces>
<!-- Existing _fiveHourResetsAt field and update sites in MainViewModel.cs -->

```csharp
// Field declaration (line 113)
private DateTimeOffset? _fiveHourResetsAt;

// Computed property (line 283)
public DateTimeOffset? FiveHourWindowStart => _fiveHourResetsAt?.AddHours(-5);

// Update site in UpdateUsagePropertiesAsync (line 504)
FiveHourCountdown = CountdownFormatter.FormatCountdown(data.FiveHour.ResetsAt);
// (immediately after) await AppendHistoryPointAsync(data.FiveHour.ResetsAt, util);

// Reset site (data.FiveHour == null branch, line 526)
_fiveHourResetsAt = null;

// Late-set in AppendHistoryPointAsync (line 609)
_fiveHourResetsAt = apiResetsAt;

// Countdown timer hook (line 626)
FiveHourCountdown = CountdownFormatter.FormatCountdown(_fiveHourResetsAt);

// IsSessionExpired flag (line 1141)
IsSessionExpired = true;
```

Existing IsSessionExpired ObservableProperty pattern:
```csharp
// IsSessionExpired is an [ObservableProperty]; partial method OnIsSessionExpiredChanged(bool value)
// can be added per CommunityToolkit.Mvvm source-generator convention.
```

WinUI3Localizer pattern (already used in this file):
```csharp
using WinUI3Localizer;
string format = Localizer.Get().GetLocalizedString("MainView.NextWindow.LabelEn");
```

CultureInfo selection pattern (from CONTEXT.md specifics block):
```csharp
using System.Globalization;
var formatKey = CultureInfo.CurrentUICulture.Name.StartsWith("de", StringComparison.OrdinalIgnoreCase)
    ? "MainView.NextWindow.LabelDe"
    : "MainView.NextWindow.LabelEn";
```

DateTimeOffset.ToString format: `ddd d.M. HH:mm` (DE custom format), `ddd HH:mm` (EN).
The `ddd` token is locale-aware — ToString MUST be called with `CultureInfo.CurrentUICulture` so
"Mo" (DE) vs "Wed" (EN) resolves correctly.
</interfaces>
</context>

<tasks>

<task type="auto" tdd="true">
  <name>Task 1: Add 2 NextWindow.* resw key pairs (DE + EN format strings)</name>
  <files>
    CCInfoWindows/CCInfoWindows/Strings/de-DE/Resources.resw,
    CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw,
    CCInfoWindows.Tests/Localization/ResourceCoverageTests.cs
  </files>
  <behavior>
    - en-US contains `MainView.NextWindow.LabelEn` = "ddd HH:mm"
    - de-DE contains `MainView.NextWindow.LabelDe` = "ddd d.M. HH:mm"
    - Both locales contain BOTH keys (parity guarantee — even though only one is used at runtime)
    - ResourceCoverageTests RequiredKeys list contains both keys, ExpectedEnUs/ExpectedDeDe dictionaries contain both expected values
    - dotnet test ResourceCoverageTests passes
  </behavior>
  <action>
Per **D-NW-03** + **CD-01**: Two-keys strategy (NOT one key with placeholders) — recommendation
accepted.

**Note on cross-locale parity** (per L10N-02): we add BOTH keys to BOTH locale files, even though
the de-DE file's `LabelEn` is never read at runtime. This keeps `ResourceCoverageTests` simple
(structural symmetry) and avoids a special-case branch in the test.

**Append to en-US/Resources.resw** (before `</root>`):
```xml
<data name="MainView.NextWindow.LabelDe" xml:space="preserve">
  <value>ddd d.M. HH:mm</value>
</data>
<data name="MainView.NextWindow.LabelEn" xml:space="preserve">
  <value>ddd HH:mm</value>
</data>
```

**Append to de-DE/Resources.resw** (before `</root>`) — IDENTICAL values (these are format
patterns, not human-readable strings):
```xml
<data name="MainView.NextWindow.LabelDe" xml:space="preserve">
  <value>ddd d.M. HH:mm</value>
</data>
<data name="MainView.NextWindow.LabelEn" xml:space="preserve">
  <value>ddd HH:mm</value>
</data>
```

**Extend ResourceCoverageTests.cs** — append to existing arrays/dictionaries (continuation of the
27-01 forward-coverage policy):

To `RequiredKeys`:
```csharp
// Phase 27 NEXTWIN-01..03: absolute next-window start label (D-NW-03 / CD-01)
"MainView.NextWindow.LabelDe",
"MainView.NextWindow.LabelEn",
```

To `ExpectedEnUs` AND `ExpectedDeDe` (same values in both — these are format patterns):
```csharp
["MainView.NextWindow.LabelDe"] = "ddd d.M. HH:mm",
["MainView.NextWindow.LabelEn"] = "ddd HH:mm",
```
  </action>
  <verify>
    <automated>dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~ResourceCoverageTests" --nologo</automated>
  </verify>
  <done>Both resw files contain both `MainView.NextWindow.Label*` keys; `ResourceCoverageTests` passes; XDocument parse remains valid.</done>
</task>

<task type="auto" tdd="true">
  <name>Task 2: Add FiveHourNextWindowText + IsFiveHourNextWindowVisible to MainViewModel + recompute helper</name>
  <files>CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs</files>
  <behavior>
    - Two new ObservableProperty fields exist (`_fiveHourNextWindowText`, `_isFiveHourNextWindowVisible`)
    - `RecomputeNextWindowLabel` private method computes localized text from `_fiveHourResetsAt` based on `CultureInfo.CurrentUICulture`
    - When `_fiveHourResetsAt is null` OR `IsSessionExpired == true` → `IsFiveHourNextWindowVisible = false`
    - When valid + auth ok → `IsFiveHourNextWindowVisible = true` and `FiveHourNextWindowText` = formatted local time
    - Method called from: (a) `UpdateUsagePropertiesAsync` after `FiveHourCountdown` is set; (b) `UpdateUsagePropertiesAsync` `else` branch (data.FiveHour == null); (c) `partial void OnIsSessionExpiredChanged(bool value)` partial method (newly added); (d) `AppendHistoryPointAsync` after `_fiveHourResetsAt = apiResetsAt;`
    - No changes to existing logout flow / IsSessionExpired setters
  </behavior>
  <action>
Per **D-NW-04** + **specifics block**: add 2 ObservableProperty fields + 1 private helper +
4 call-sites + 1 partial method.

**Add new field declarations** in the 5-hour-window region (insert after the existing
`_fiveHourCountdown` declaration around line 111, before the existing `private DateTimeOffset? _fiveHourResetsAt;`
line 113):

```csharp
// NEXTWIN-01..03: absolute reset-time label below the countdown (D-NW-04)
[ObservableProperty]
private string _fiveHourNextWindowText = string.Empty;

[ObservableProperty]
private bool _isFiveHourNextWindowVisible;
```

**Add `using System.Globalization;`** at the top of the file if not already present (verify with
grep; the file already uses `CultureInfo` for other Localizer calls — likely present).

**Add the recompute helper** as a private method, placed near `UpdateCountdowns` (around line 626)
or in the helper region. Suggested location: directly after `UpdateCountdowns` method:

```csharp
/// <summary>
/// NEXTWIN-01..03 (D-NW-02..04): recomputes the absolute next-window label from
/// _fiveHourResetsAt. Hides the label (Visibility=Collapsed) when ResetsAt is null OR
/// IsSessionExpired is true (auth banner takes priority — banner-stack alignment with PRICING).
/// Format pattern is loaded from MainView.NextWindow.Label{De,En} resw key based on
/// CultureInfo.CurrentUICulture.
/// </summary>
private void RecomputeNextWindowLabel()
{
    if (_fiveHourResetsAt is null || IsSessionExpired)
    {
        IsFiveHourNextWindowVisible = false;
        FiveHourNextWindowText = string.Empty;
        return;
    }

    var culture = CultureInfo.CurrentUICulture;
    var formatKey = culture.Name.StartsWith("de", StringComparison.OrdinalIgnoreCase)
        ? "MainView.NextWindow.LabelDe"
        : "MainView.NextWindow.LabelEn";
    var format = Localizer.Get().GetLocalizedString(formatKey);

    FiveHourNextWindowText = _fiveHourResetsAt.Value.LocalDateTime.ToString(format, culture);
    IsFiveHourNextWindowVisible = true;
}
```

**Add a partial method** for `IsSessionExpired` change notification (CommunityToolkit.Mvvm
source-generator pattern). Place near other `On*Changed` partials, or at the bottom of the class:

```csharp
// NEXTWIN-02 (D-NW-02): hide the next-window label when auth banner appears.
partial void OnIsSessionExpiredChanged(bool value) => RecomputeNextWindowLabel();
```

**Wire 4 call-sites** — add `RecomputeNextWindowLabel();` AFTER each of these existing lines:

1. **Line ~526** (in `UpdateUsagePropertiesAsync`, the `else` branch after `_fiveHourResetsAt = null;`):
   ```csharp
   _fiveHourResetsAt = null;
   RecomputeNextWindowLabel();   // NEW (NEXTWIN — clears label when API returns no FiveHour)
   ```

2. **Line ~609** (in `AppendHistoryPointAsync` after `_fiveHourResetsAt = apiResetsAt;`):
   ```csharp
   _fiveHourResetsAt = apiResetsAt;
   RecomputeNextWindowLabel();   // NEW (NEXTWIN — recomputes when fresh API resetsAt arrives)
   ```

3. **Line ~626** (in `UpdateCountdowns`, after `FiveHourCountdown = CountdownFormatter.FormatCountdown(_fiveHourResetsAt);`):
   ```csharp
   FiveHourCountdown = CountdownFormatter.FormatCountdown(_fiveHourResetsAt);
   RecomputeNextWindowLabel();   // NEW (NEXTWIN — keep absolute label live during countdown ticks; defensive against late ResetsAt hydration)
   ```

4. **Line ~379** (in `InitializeAsync`, the `if (history.ResetsAt.HasValue) { _fiveHourResetsAt = history.ResetsAt; }` block):
   ```csharp
   if (history.ResetsAt.HasValue)
   {
       _fiveHourResetsAt = history.ResetsAt;
   }
   RecomputeNextWindowLabel();   // NEW (NEXTWIN — show absolute label from persisted history at cold start)
   ```

**Read `_fiveHourResetsAt` only via the field** — DO NOT introduce a setter wrapper. Existing
field-direct mutations are preserved.

**Per L-02 (G-1):** This plan adds NO new `IRecipient<>` handlers — `RecomputeNextWindowLabel`
runs synchronously on whatever thread already mutates `_fiveHourResetsAt` (the existing call
chain runs on the UI thread because it's downstream of `_dispatcherQueue.TryEnqueue` calls from
the timer/poll cascade). No additional `_dispatcherQueue` wrapping is needed.
  </action>
  <verify>
    <automated>dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj --nologo</automated>
  </verify>
  <done>Build is green. All 4 call-sites + the OnIsSessionExpiredChanged partial method are present. ObservableProperty fields appear in IL via source generator (verifiable by inspecting `obj/Debug/.../MainViewModel.g.cs` if needed).</done>
</task>

<task type="auto" tdd="true">
  <name>Task 3: Add NextWindow TextBlock to MainView.xaml directly below the existing FiveHourCountdown</name>
  <files>CCInfoWindows/CCInfoWindows/Views/MainView.xaml</files>
  <behavior>
    - A TextBlock element exists below the existing FiveHourCountdown (line 335) bound to `ViewModel.FiveHourNextWindowText`
    - Its Visibility is bound to `ViewModel.IsFiveHourNextWindowVisible` via the existing `BoolToVisibilityConverter`
    - Layout: same right-aligned position as the countdown, smaller font (FontSize 11), SecondaryTextBrush
    - When the app launches with valid 5h data, the new TextBlock renders below the countdown
    - When IsSessionExpired flips true (or ResetsAt becomes null), the TextBlock collapses
  </behavior>
  <action>
Per **D-NW-01**: place the new TextBlock immediately below the existing FiveHourCountdown, in the
same right-aligned StackPanel column. The existing `Grid` at MainView.xaml lines 316-339 has 2
columns: column 0 = percentage, column 1 = countdown stack. We extend column 1 with a vertical
stack so the absolute label sits beneath the countdown.

**Edit the Grid at lines 316-339**: replace the `<StackPanel Grid.Column="1" Orientation="Horizontal" ...>`
block (lines 330-338) with a vertical container holding the existing horizontal countdown stack
PLUS the new absolute-time TextBlock:

```xml
<!-- Countdown stack with absolute-time label (NEXTWIN-01..03 / D-NW-01..04) -->
<StackPanel Grid.Column="1" Orientation="Vertical"
            HorizontalAlignment="Right" VerticalAlignment="Center" Spacing="2">
    <!-- Existing horizontal countdown stack (preserved verbatim) -->
    <StackPanel Orientation="Horizontal"
                HorizontalAlignment="Right" VerticalAlignment="Center" Spacing="4">
        <FontIcon Glyph="&#xE823;" FontSize="13"
                  Foreground="{ThemeResource SecondaryTextBrush}" />
        <TextBlock
            Text="{x:Bind ViewModel.FiveHourCountdown, Mode=OneWay}"
            FontSize="13"
            Foreground="{ThemeResource SecondaryTextBrush}" />
    </StackPanel>

    <!-- NEXTWIN-01..03 / D-NW-01: absolute reset-time below countdown -->
    <TextBlock
        Text="{x:Bind ViewModel.FiveHourNextWindowText, Mode=OneWay}"
        Visibility="{x:Bind ViewModel.IsFiveHourNextWindowVisible, Mode=OneWay,
                     Converter={StaticResource BoolToVisibilityConverter}}"
        FontSize="11"
        HorizontalAlignment="Right"
        Foreground="{ThemeResource SecondaryTextBrush}" />
</StackPanel>
```

**DO NOT use `l:Uids.Uid`** for the new TextBlock — its `Text` is a runtime-computed value from the
ViewModel, NOT a static localized string. The localization happens inside `RecomputeNextWindowLabel`
via the format key (D-NW-03 / CD-01).

**No code-behind changes** — this is a pure XAML edit + binding. Per CLAUDE.md "No code-behind
logic in Views" rule.
  </action>
  <verify>
    <automated>dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj --nologo</automated>
  </verify>
  <done>Build is green. MainView.xaml contains a TextBlock with `x:Bind ViewModel.FiveHourNextWindowText` directly below the FiveHourCountdown TextBlock, governed by `IsFiveHourNextWindowVisible`. No XAML errors at compile time.</done>
</task>

</tasks>

<threat_model>
## Trust Boundaries

| Boundary | Description |
|----------|-------------|
| API → MainViewModel | `UsageResponse.FiveHour.ResetsAt` is `DateTimeOffset?` deserialized from claude.ai response (validated upstream by JSON deserializer) |
| ViewModel → XAML binding | `FiveHourNextWindowText` is a string set only inside `RecomputeNextWindowLabel`; not user-controlled |

## STRIDE Threat Register

| Threat ID | Category | Component | Disposition | Mitigation Plan |
|-----------|----------|-----------|-------------|-----------------|
| T-27-02-01 | Tampering | API ResetsAt value | accept | Trusted internal HTTPS source (claude.ai); JSON deserialization validates type; null already handled |
| T-27-02-02 | Information Disclosure | absolute reset time displayed | accept | Reset time is non-sensitive (already shown as countdown, line 335) |
| T-27-02-03 | Denial of Service | malformed format string from resw causes ToString to throw | mitigate | resw values are author-controlled (read-only bundled assets); ToString only called with `CultureInfo.CurrentUICulture` (always non-null) |
| T-27-02-04 | Elevation of Privilege | n/a | accept | Pure read-only display logic; no privilege boundary crossed |
</threat_model>

<verification>
Run `dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` — must succeed.
Run `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~ResourceCoverageTests" --nologo` — all green.
Manual smoke: launch app, login, observe absolute time appears below the existing 5h countdown — recheck after manually triggering a 401 (auth banner shows → label collapses).
</verification>

<success_criteria>
1. NEXTWIN-01: TextBlock visible below countdown, sourcing from `UsageResponse.FiveHour.ResetsAt`
2. NEXTWIN-02: TextBlock collapsed (NOT "—") when `_fiveHourResetsAt is null` or `IsSessionExpired`
3. NEXTWIN-03: format auto-switches DE/EN via `CultureInfo.CurrentUICulture`
4. ResourceCoverageTests passes for both new keys
5. Build is green
</success_criteria>

<output>
After completion, create `.planning/phases/27-nextwin-orgid-pricing-l10n/27-02-SUMMARY.md` documenting:
- 2 new resw keys added (DE + EN values)
- ObservableProperty fields added to MainViewModel
- 4 call-sites + 1 partial method added
- XAML container restructure (horizontal → vertical stack in Grid.Column 1)
</output>
