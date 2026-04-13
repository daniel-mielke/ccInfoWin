# Phase 18: Settings Redesign - Research

**Researched:** 2026-04-13
**Domain:** WinUI 3 Segmented Control, XAML layout, MVVM, localization
**Confidence:** HIGH

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Tab Structure & Content**
- Tab order: General, Updates, Account, About — per macOS Spec FEAT-03a
- General tab: all existing settings (refresh interval, session timeout, theme, language, Sonnet context, autostart) in uniform 40px rows
- Updates tab: app version, pricing source info, last pricing fetch timestamp
- Account tab: token status and logout button
- About tab: app name, version, GitHub link, macOS original credits

**Segmented Control Implementation**
- Use `CommunityToolkit.WinUI.Controls.Segmented` — already installed in project
- Content switching via Visibility toggle on 4 StackPanels — no Frame navigation needed
- Colored icon badges per tab (FEAT-03c) — inline DataTemplate with colored FontIcon
- Badge colors: Green (General), Blue (Updates), Red (Account), Orange (About) — new theme brushes in AppTheme.xaml

**Layout & Localization**
- Fixed 360px width for settings area
- Uniform 40px height rows: label left-aligned, control right-aligned
- Short time notation: "30s", "1min", "5min" etc. — replaces current "30 Sekunden" long format
- All new tab labels and content use `l:Uids.Uid` for runtime DE/EN switching

### Claude's Discretion

None — all decisions were locked in discussion.

### Deferred Ideas (OUT OF SCOPE)

None — discussion stayed within phase scope.
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| SETT-01 | User sees a Segmented Control with 4 tabs (General, Updates, Account, About) with colored icon badges | CommunityToolkit.WinUI.Controls.Segmented 8.2.251219 already installed; SegmentedItem supports Icon property; colored badge via inline Border+FontIcon in ItemTemplate |
| SETT-02 | General tab contains all existing settings with uniform 40px row height (label left, control right) | Current SettingsView.xaml has all 7 rows; needs Grid-based layout with Height="40" uniform rows |
| SETT-03 | Dropdown values use short time notation (30s, 1min, 5min, 15min, 30min, etc.) | RefreshOptions list in SettingsViewModel must replace labels; timeout ComboBoxItems need new Uid text |
| SETT-04 | Updates tab shows app version, pricing source info, and last pricing fetch timestamp | IPricingService already exposes Source+LastFetch; app version via Assembly.GetExecutingAssembly().GetName().Version |
| SETT-05 | Account tab shows token status and logout button | ICredentialService.HasValidToken() already available; LogoutCommand exists |
| SETT-06 | About tab shows app name, version, GitHub link, and credits for macOS original | Static content + version string; HyperlinkButton for GitHub URL |
| SETT-07 | Tab switching is smooth without page reload, all tabs fit within 360px window width | Visibility-based panel switching via BoolToVisibilityConverter or converter-free x:Bind with int comparison |
| SETT-08 | All settings tab labels and content are localized in German and English | 14 new resw keys needed across both language files |
</phase_requirements>

---

## Summary

Phase 18 replaces the flat single-page SettingsView with a Segmented Control at the top (4 tabs) and tab-specific content panels below. The implementation is a complete XAML rewrite of `SettingsView.xaml` plus targeted ViewModel changes — no new services required. All dependencies are already in place: the Segmented package is installed, all service interfaces expose the needed data, and the localization infrastructure is established.

The key architectural decision (already locked): tab switching uses Visibility-toggled StackPanels, not Frame navigation or SwitchPresenter. `SwitchPresenter` lives in `CommunityToolkit.WinUI.Controls.Primitives`, which is NOT installed — do not add it. The Visibility approach binds `SelectedTabIndex` (int) in the ViewModel to 4 panels using a helper or `x:Bind` with inline comparison.

The colored badge icons use a `Border` + white `FontIcon` inline inside `SegmentedItem`. The `Icon` property of `SegmentedItem` accepts any `IconElement`, but to use an arbitrary `UIElement` (like a `Border`), the icon must be wrapped using an `IconSourceElement` or placed as a custom `DataTemplate`. The safest approach for WinUI 3 is to use `SegmentedItem.Content` as a custom `StackPanel` containing both the badge and text, bypassing the `Icon` property entirely.

**Primary recommendation:** Complete XAML rewrite of `SettingsView.xaml`. Add `SelectedTabIndex` int property to `SettingsViewModel`. Add `TokenStatusText`/`IsTokenValid` to ViewModel for Account tab. Update `RefreshOptions` labels to short notation. Add 4 badge brushes to AppTheme.xaml. Add 14+ localization keys to both .resw files.

---

## Standard Stack

### Core (already installed — no new packages needed)

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| CommunityToolkit.WinUI.Controls.Segmented | 8.2.251219 | Segmented tab control | Already in project |
| CommunityToolkit.Mvvm | 8.4.0 | MVVM source generators | Project-wide pattern |
| WinUI3Localizer | 2.3.0 | Runtime DE/EN switching | Project-wide pattern |
| Microsoft.WindowsAppSDK | 1.8.260209005 | WinUI 3 + assembly metadata | Project-wide |

### Not Available (do not add)

| Package | Reason |
|---------|--------|
| CommunityToolkit.WinUI.Controls.Primitives | Contains `SwitchPresenter` — not installed, not needed |
| CommunityToolkit.WinUI.Animations | Not installed — skip implicit animations on tab panels |

---

## Architecture Patterns

### Segmented Control Usage (verified from official docs)

The `Segmented` control accepts `SegmentedItem` children. Each item supports:
- `Content` — text label or arbitrary content (StackPanel allowed)
- `Icon` — accepts `IconElement` subtypes (FontIcon, BitmapIcon, etc.)
- `Tag` — string tag for identification

**Namespace required in XAML:**
```xml
xmlns:controls="using:CommunityToolkit.WinUI.Controls"
```

**TwoWay SelectedIndex binding:**
```xml
<controls:Segmented
    HorizontalAlignment="Stretch"
    SelectedIndex="{x:Bind ViewModel.SelectedTabIndex, Mode=TwoWay}">
```

### Colored Badge Approach (inline UIElement in Content)

The `Icon` property of `SegmentedItem` is typed as `IconElement`, which does NOT accept a `Border`. To embed a colored badge, replace `Icon` with a custom `Content` that contains both the badge and label in a `StackPanel`:

```xml
<controls:SegmentedItem Tag="general">
    <controls:SegmentedItem.Content>
        <StackPanel Orientation="Horizontal" Spacing="6" VerticalAlignment="Center">
            <Border Width="18" Height="18" CornerRadius="4"
                    Background="{ThemeResource SettingsBadgeGreenBrush}">
                <FontIcon Glyph="&#xE713;" FontSize="10" Foreground="White"
                          HorizontalAlignment="Center" VerticalAlignment="Center" />
            </Border>
            <TextBlock l:Uids.Uid="SettingsTabGeneral" VerticalAlignment="Center" />
        </StackPanel>
    </controls:SegmentedItem.Content>
</controls:SegmentedItem>
```

Source: CommunityToolkit official docs (https://learn.microsoft.com/en-us/dotnet/communitytoolkit/windows/segmented) — `Content` property accepts arbitrary UIElement.

### Tab Content Visibility Switching

Use `BoolToVisibilityConverter` (already in the project at `Converters/BoolToVisibilityConverter.cs`) with a computed bool property per tab, or use an `IntToVisibilityConverter` (needs creating). The simplest approach is 4 computed bool properties on the ViewModel:

```csharp
// In SettingsViewModel
[ObservableProperty]
private int _selectedTabIndex = 0;

public bool IsGeneralTabVisible => _selectedTabIndex == 0;
public bool IsUpdatesTabVisible => _selectedTabIndex == 1;
public bool IsAccountTabVisible => _selectedTabIndex == 2;
public bool IsAboutTabVisible  => _selectedTabIndex == 3;

partial void OnSelectedTabIndexChanged(int value)
{
    OnPropertyChanged(nameof(IsGeneralTabVisible));
    OnPropertyChanged(nameof(IsUpdatesTabVisible));
    OnPropertyChanged(nameof(IsAccountTabVisible));
    OnPropertyChanged(nameof(IsAboutTabVisible));
}
```

```xml
<StackPanel Visibility="{x:Bind ViewModel.IsGeneralTabVisible, Mode=OneWay,
    Converter={StaticResource BoolToVisibilityConverter}}">
    <!-- General tab content -->
</StackPanel>
```

**Alternative (converter-free):** Use `x:Bind` with inline `int` comparison in `Visibility` attribute. Not supported natively — requires a converter. Stick with bool properties.

### Uniform 40px Row Pattern

From macOS spec FEAT-03f — enforced with `Grid` rows:

```xml
<Grid Height="40" Padding="12,0">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*" />
        <ColumnDefinition Width="Auto" />
    </Grid.ColumnDefinitions>
    <TextBlock l:Uids.Uid="SettingsRefreshInterval"
               FontSize="13" Foreground="{ThemeResource PrimaryTextBrush}"
               VerticalAlignment="Center" />
    <ComboBox Grid.Column="1" VerticalAlignment="Center"
              ItemsSource="{x:Bind ViewModel.RefreshOptions}"
              SelectedItem="{x:Bind ViewModel.SelectedRefreshOption, Mode=TwoWay}"
              DisplayMemberPath="Label" />
</Grid>
```

All 7 General tab rows follow this pattern with separator `Border` (Height="1", DividerBrush) between them.

### Card-Style Group Container

From macOS spec FEAT-03f:

```xml
<Border CornerRadius="8"
        Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
        Margin="0,8,0,0">
    <!-- Rows inside -->
</Border>
```

`CardBackgroundFillColorDefaultBrush` is a built-in WinUI 3 theme resource — no custom brush needed.

### App Version Retrieval

For the Updates and About tabs, app version comes from the assembly:

```csharp
// In SettingsViewModel constructor or as computed property
public string AppVersionText =>
    System.Reflection.Assembly.GetExecutingAssembly()
        .GetName().Version?.ToString(3) ?? "1.0.0";
```

The `.csproj` has `<Version>1.0.0</Version>` — this flows into `AssemblyInformationalVersion`. The `Version` property on `AssemblyName` comes from `<AssemblyVersion>1.0.0.0</AssemblyVersion>`. ToString(3) gives "1.0.0" (major.minor.patch).

### Token Status for Account Tab

`ICredentialService.HasValidToken()` is available via DI in `SettingsViewModel`. Add:

```csharp
public bool IsTokenValid => _credentialService.HasValidToken();
public string TokenStatusText => IsTokenValid ? "gültig" / "valid" : "ungültig" / "invalid";
```

For localization, `TokenStatusText` should use localization keys rather than hardcoded strings. Add `SettingsTokenValid` and `SettingsTokenInvalid` resw keys. The ViewModel can use `Localizer.Get().GetLocalizedString(key)` — but the simpler pattern used in this project is to expose a bool and let the XAML use a converter or dual TextBlocks with Visibility.

### GitHub HyperlinkButton for About Tab

```xml
<HyperlinkButton NavigateUri="https://github.com/your-repo/ccInfoWin"
                 HorizontalAlignment="Left">
    <TextBlock l:Uids.Uid="SettingsAboutGitHubLink" />
</HyperlinkButton>
```

`HyperlinkButton` with `NavigateUri` launches the default browser — no code-behind needed.

### Recommended Project Structure Changes

```
Views/
  SettingsView.xaml          -- Complete rewrite (tab structure)
  SettingsView.xaml.cs       -- No changes needed (already minimal)
ViewModels/
  SettingsViewModel.cs       -- Add SelectedTabIndex, tab visibility props, AppVersionText, IsTokenValid
Resources/
  AppTheme.xaml              -- Add 4 SettingsBadge*Brush resources (Dark + Light)
Strings/
  de-DE/Resources.resw       -- Add 14 new keys
  en-US/Resources.resw       -- Add 14 new keys
```

### Anti-Patterns to Avoid

- **Don't use SwitchPresenter:** Not installed, adds a NuGet dependency for a simple feature.
- **Don't use Frame navigation for tabs:** Context.md explicitly forbids it; creates page-reload UX.
- **Don't use `Icon` property with Border:** `SegmentedItem.Icon` expects `IconElement`, not `UIElement`. Use `Content` StackPanel instead.
- **Don't hardcode version strings:** Always read from assembly metadata.
- **Don't localize "s"/"min" suffixes:** Per spec FEAT-03d, short time labels (30s, 1min, etc.) are language-independent. Use plain strings in `RefreshOptions` and `TimeoutOptions`.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Tab content switching | Custom ContentPresenter + Frame | BoolToVisibilityConverter on 4 panels | Already in project, zero extra code |
| Short time labels | Localization keys per time value | Plain string labels in ViewModel list | Universal abbreviations, no l10n needed |
| Token status display | Custom status service | `ICredentialService.HasValidToken()` | Already in project |
| App version display | Custom version service | `Assembly.GetExecutingAssembly().GetName().Version` | .NET reflection, always correct |

**Key insight:** Everything needed is already in the project. Phase 18 is a XAML + ViewModel change, not a service change.

---

## Current State Inventory

### What exists in SettingsView.xaml (to be replaced)

| Section | Current structure | New location |
|---------|-------------------|--------------|
| Back button + title | Row 0 StackPanel | Preserved as-is |
| Refresh interval ComboBox | Row 1, standalone StackPanel | General tab row 2 |
| Session timeout ComboBox | Row 1, standalone StackPanel | General tab row 3 |
| Dark mode ToggleSwitch | Row 1, Grid 2-col | General tab row 4 |
| Pricing info (source + last fetch) | Row 1, Grid 2-row | **Moved to Updates tab** |
| ANWENDUNG section | StackPanel with heading | General tab (autostart, language, sonnet, reset) |
| Logout button | Row 2 bottom Button | **Moved to Account tab** |

### Properties/commands in SettingsViewModel (all preserved)

| Property/Command | Status | Target Tab |
|------------------|--------|------------|
| `RefreshOptions` (List<RefreshOption>) | Labels to update | General |
| `SelectedRefreshOption` | Keep as-is | General |
| `IsDarkMode` | Keep as-is | General |
| `SelectedThresholdIndex` | Keep as-is; ComboBox inline items → labels update | General |
| `IsAutostart` | Keep as-is | General |
| `SelectedLanguageIndex` | Keep as-is | General |
| `SelectedSonnetContextIndex` | Keep as-is | General |
| `PricingSourceText` (computed) | Keep as-is | Updates |
| `LastPricingFetchText` (computed) | Keep as-is | Updates |
| `LogoutCommand` | Keep as-is | Account |
| `GoBackCommand` | Keep as-is | Header |
| `ResetWindowSizeCommand` | Keep as-is | General |

### New items to add to SettingsViewModel

| Item | Type | Purpose |
|------|------|---------|
| `SelectedTabIndex` | `[ObservableProperty] int` | Drives Visibility of 4 panels |
| `IsGeneralTabVisible` | `bool` (computed) | Panel 0 visibility |
| `IsUpdatesTabVisible` | `bool` (computed) | Panel 1 visibility |
| `IsAccountTabVisible` | `bool` (computed) | Panel 2 visibility |
| `IsAboutTabVisible` | `bool` (computed) | Panel 3 visibility |
| `AppVersionText` | `string` (computed, static) | Updates + About tab |
| `IsTokenValid` | `bool` (computed) | Account tab |

---

## Common Pitfalls

### Pitfall 1: SegmentedItem.Icon with non-IconElement
**What goes wrong:** Placing a `Border` or `StackPanel` directly in `Icon="{...}"` throws a XAML parse error because `Icon` is typed as `IconElement`.
**Why it happens:** The WinUI 3 `SegmentedItem.Icon` property is `IconElement`, not `object`.
**How to avoid:** Use `SegmentedItem.Content` as a custom `StackPanel` with the badge + text inside. Omit the `Icon` property entirely.
**Warning signs:** XAML design-time error "Cannot set property 'Icon' of type 'IconElement'."

### Pitfall 2: BoolToVisibilityConverter direction
**What goes wrong:** `IsGeneralTabVisible = true` shows the panel, but the converter may be inverted.
**Why it happens:** The project has both `BoolToVisibilityConverter` and `InvertedBoolToVisibilityConverter`.
**How to avoid:** Use `BoolToVisibilityConverter` (non-inverted) for tab panels.
**Warning signs:** Wrong panel showing; all panels hidden on startup.

### Pitfall 3: SelectedTabIndex not initialized
**What goes wrong:** Segmented Control shows no selection on first open.
**Why it happens:** `_selectedTabIndex` defaults to 0, but Segmented may not fire `SelectionChanged` if 0 is already set before the control loads.
**How to avoid:** Set `SelectedIndex="0"` in XAML as well as the default in ViewModel. The `SegmentedItem` at index 0 will be selected automatically.

### Pitfall 4: TimeoutOptions localized keys vs. short labels
**What goes wrong:** Timeout ComboBox still shows "15 Minuten" / "15 minutes" because it uses `ComboBoxItem l:Uids.Uid="Timeout15"`.
**Why it happens:** The resw keys (`Timeout15.Content`) have long-form values.
**How to avoid:** Either update the resw values to "15min" (universal) or change the ComboBox to use inline `Content="15min"` without Uid. Per spec FEAT-03d, short labels are language-independent — remove the Uid and hardcode "15min", "30min", "60min", "120min".

### Pitfall 5: Pricing section removed from General tab
**What goes wrong:** PricingSourceText and LastPricingFetchText bindings disappear from view if not added to Updates tab.
**Why it happens:** The current XAML has them in the General area; the new XAML reorganizes into tabs.
**How to avoid:** Explicitly add the pricing Grid to the Updates tab content panel — don't forget it.

### Pitfall 6: ToggleSwitch width in 40px rows
**What goes wrong:** `ToggleSwitch` has a default MinWidth of ~154px that overflows 360px layout.
**Why it happens:** WinUI 3 ToggleSwitch has a wide default MinWidth.
**How to avoid:** Set `MinWidth="0"` and `OnContent=""` / `OffContent=""` on the ToggleSwitch. The current XAML already does this for DarkModeToggle — replicate for AutostartToggle.

### Pitfall 7: WinUI3Localizer and TextBlock Uid inside SegmentedItem.Content
**What goes wrong:** `l:Uids.Uid` on a TextBlock inside `SegmentedItem.Content` may not resolve if the Uid infrastructure isn't crawled inside the custom content template.
**Why it happens:** WinUI3Localizer walks the visual tree; custom `SegmentedItem.Content` templates are usually included in the walk, but the behavior should be verified.
**How to avoid:** Test tab labels update when switching language. If Uid resolution fails inside SegmentedItem, fall back to `x:Bind ViewModel.GeneralTabLabel` (string property updated via WeakReferenceMessenger on language change).
**Warning signs:** Tab labels stay in DE after switching to EN (or vice versa).

---

## Code Examples

### Complete Segmented Control Structure

```xml
<!-- Source: CommunityToolkit official docs + project patterns -->
<controls:Segmented
    xmlns:controls="using:CommunityToolkit.WinUI.Controls"
    HorizontalAlignment="Stretch"
    Margin="0,0,0,12"
    SelectedIndex="{x:Bind ViewModel.SelectedTabIndex, Mode=TwoWay}">

    <controls:SegmentedItem>
        <controls:SegmentedItem.Content>
            <StackPanel Orientation="Horizontal" Spacing="6" VerticalAlignment="Center">
                <Border Width="18" Height="18" CornerRadius="4"
                        Background="{ThemeResource SettingsBadgeGreenBrush}">
                    <FontIcon Glyph="&#xE713;" FontSize="10" Foreground="White"
                              HorizontalAlignment="Center" VerticalAlignment="Center" />
                </Border>
                <TextBlock l:Uids.Uid="SettingsTabGeneral" VerticalAlignment="Center" FontSize="12" />
            </StackPanel>
        </controls:SegmentedItem.Content>
    </controls:SegmentedItem>

    <!-- Repeat for Updates (\uE895, Blue), Account (\uE77B, Red), About (\uE946, Orange) -->
</controls:Segmented>
```

### Tab Content Panel (General)

```xml
<StackPanel Visibility="{x:Bind ViewModel.IsGeneralTabVisible, Mode=OneWay,
                Converter={StaticResource BoolToVisibilityConverter}}">
    <TextBlock l:Uids.Uid="SettingsTabGeneral"
               FontSize="11" FontWeight="SemiBold"
               Foreground="{ThemeResource SectionHeaderBrush}"
               CharacterSpacing="50" Margin="0,0,0,8" />
    <Border CornerRadius="8"
            Background="{ThemeResource CardBackgroundFillColorDefaultBrush}">
        <StackPanel>
            <!-- Row 1: Autostart -->
            <Grid Height="40" Padding="12,0">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <TextBlock l:Uids.Uid="SettingsAutostartLabel"
                           FontSize="13" Foreground="{ThemeResource PrimaryTextBrush}"
                           VerticalAlignment="Center" />
                <ToggleSwitch Grid.Column="1" MinWidth="0"
                              l:Uids.Uid="AutostartToggle"
                              IsOn="{x:Bind ViewModel.IsAutostart, Mode=TwoWay}"
                              OnContent="" OffContent="" />
            </Grid>
            <Border Height="1" Background="{ThemeResource DividerBrush}" Margin="12,0" />
            <!-- ... more rows ... -->
        </StackPanel>
    </Border>
</StackPanel>
```

### AppTheme.xaml Badge Brush Additions

```xml
<!-- Add inside both Dark and Light ResourceDictionary sections -->

<!-- Dark theme additions -->
<SolidColorBrush x:Key="SettingsBadgeGreenBrush"  Color="#30D158" />
<SolidColorBrush x:Key="SettingsBadgeBlueBrush"   Color="#0A84FF" />
<SolidColorBrush x:Key="SettingsBadgeRedBrush"    Color="#FF453A" />
<SolidColorBrush x:Key="SettingsBadgeOrangeBrush" Color="#FF9F0A" />

<!-- Light theme additions -->
<SolidColorBrush x:Key="SettingsBadgeGreenBrush"  Color="#34C759" />
<SolidColorBrush x:Key="SettingsBadgeBlueBrush"   Color="#007AFF" />
<SolidColorBrush x:Key="SettingsBadgeRedBrush"    Color="#FF3B30" />
<SolidColorBrush x:Key="SettingsBadgeOrangeBrush" Color="#FF9500" />
```

Source: macOS spec FEAT-03a + AppTheme.xaml existing pattern

### Localization Keys to Add (both .resw files)

| Key | DE value | EN value |
|-----|----------|----------|
| `SettingsTabGeneral.Content` (or Text) | Allgemein | General |
| `SettingsTabUpdates.Content` | Updates | Updates |
| `SettingsTabAccount.Content` | Konto | Account |
| `SettingsTabAbout.Content` | Über | About |
| `SettingsGeneralSectionHeader.Text` | ALLGEMEIN | GENERAL |
| `SettingsUpdatesSectionHeader.Text` | UPDATES | UPDATES |
| `SettingsAccountSectionHeader.Text` | KONTO | ACCOUNT |
| `SettingsAboutSectionHeader.Text` | ÜBER | ABOUT |
| `SettingsAboutAppName.Text` | CCInfoWindows | CCInfoWindows |
| `SettingsAboutGitHubLink.Content` | GitHub-Repository | GitHub Repository |
| `SettingsAboutCredits.Text` | Basierend auf ccInfo für macOS von Stefan Lange | Based on ccInfo for macOS by Stefan Lange |
| `SettingsTokenStatus.Text` | Token-Status: | Token status: |
| `SettingsTokenValid.Text` | Gültig | Valid |
| `SettingsTokenInvalid.Text` | Ungültig | Invalid |
| `SettingsVersion.Text` | Version: | Version: |
| `SettingsPricingSourceHeader.Text` | Preisdaten: | Pricing data: |
| `SettingsLastFetchHeader.Text` | Zuletzt aktualisiert: | Last updated: |

**Note:** The existing `SettingsPricingSource` and `SettingsLastFetch` keys can be reused for the Updates tab if their text values still apply.

### Keys to Remove or Leave Unchanged

The existing `Timeout15`/`Timeout30`/`Timeout60`/`Timeout120` Uid keys should be **removed from XAML** and replaced with plain `Content="15min"` etc. (language-independent per spec). The resw entries can be left (orphaned) or cleaned up.

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Flat StackPanel with all settings | Segmented Control with 4 tabs | Phase 18 | Organized, scannable |
| Long time labels ("30 Sekunden") | Short notation ("30s", "1min") | Phase 18 | Fits 40px row width |
| Logout in footer of every page | Logout in Account tab | Phase 18 | Cleaner separation |
| Pricing info in main settings flow | Pricing info in Updates tab | Phase 18 | Logical grouping |

---

## Open Questions

1. **WinUI3Localizer + SegmentedItem.Content TextBlock**
   - What we know: `l:Uids.Uid` works on all standard elements crawled by the localizer
   - What's unclear: Whether `SegmentedItem.Content` (custom StackPanel) is walked by WinUI3Localizer's tree crawler
   - Recommendation: Implement with Uid first; add a language-change test. If it fails, expose tab label strings as ViewModel properties updated via WeakReferenceMessenger.

2. **ToggleSwitch width in 40px rows**
   - What we know: Current DarkModeToggle already uses `OnContent=""` `OffContent=""` — this is the fix
   - What's unclear: Whether MinWidth override is also needed for AutostartToggle
   - Recommendation: Apply `MinWidth="0"` to both ToggleSwitches as a precaution.

3. **GitHub repository URL**
   - What we know: About tab needs a GitHub link
   - What's unclear: The exact repo URL (project is private/personal)
   - Recommendation: Use a placeholder `https://github.com/dmielke/ccInfoWin` or make it a config constant. Check existing CLAUDE.md/README for the actual URL.

---

## Environment Availability

Step 2.6: SKIPPED — Phase 18 is a pure XAML/C# code change. No external tools, runtimes, databases, or CLI utilities beyond the existing .NET 9 + Windows App SDK toolchain.

---

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xunit 2.9.3 + Moq 4.20.72 |
| Config file | `CCInfoWindows.Tests/CCInfoWindows.Tests.csproj` |
| Quick run command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -x64 --filter "FullyQualifiedName~SettingsViewModel"` |
| Full suite command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -x64` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| SETT-01 | Segmented Control with 4 tabs visible | manual | Visual verification (UI) | N/A |
| SETT-02 | General tab has all 7 existing settings | manual | Visual verification (UI) | N/A |
| SETT-03 | RefreshOptions list uses short notation (30s, 1min, etc.) | unit | `dotnet test ... --filter "FullyQualifiedName~SettingsViewModelTests"` | ❌ Wave 0 |
| SETT-04 | AppVersionText returns non-empty string; PricingSourceText exposes Live/Fallback | unit | same | ❌ Wave 0 |
| SETT-05 | IsTokenValid reflects credential state | unit | same | ❌ Wave 0 |
| SETT-06 | AppVersionText returns semver string | unit | same | ❌ Wave 0 |
| SETT-07 | Tab switching updates IsGeneralTabVisible..IsAboutTabVisible correctly | unit | same | ❌ Wave 0 |
| SETT-08 | Localization keys exist in both .resw files | manual | File inspection / build | N/A (resw validation at build) |

### Sampling Rate

- **Per task commit:** `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --filter "FullyQualifiedName~Settings" -x64`
- **Per wave merge:** `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj -x64`
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps

- [ ] `CCInfoWindows.Tests/ViewModels/SettingsViewModelTests.cs` — covers SETT-03, SETT-04, SETT-05, SETT-06, SETT-07
  - `TabSwitching_UpdatesVisibilityProps` (SETT-07)
  - `RefreshOptions_UseShortNotation` (SETT-03)
  - `AppVersionText_ReturnsNonEmpty` (SETT-04, SETT-06)
  - `IsTokenValid_ReflectsCredentialService` (SETT-05)

---

## Sources

### Primary (HIGH confidence)
- CommunityToolkit Segmented docs (https://learn.microsoft.com/en-us/dotnet/communitytoolkit/windows/segmented) — SegmentedItem API, Content property, SelectedIndex binding
- `CCInfoWindows.csproj` — confirmed CommunityToolkit.WinUI.Controls.Segmented 8.2.251219 installed
- `SettingsView.xaml` — full inventory of current controls and bindings
- `SettingsViewModel.cs` — all existing properties, commands, DI dependencies
- `AppTheme.xaml` — existing brush pattern (Dark/Light ResourceDictionary)
- `de-DE/Resources.resw` + `en-US/Resources.resw` — all existing localization keys
- `IPricingService.cs` — Source (PricingSource enum) + LastFetch (DateTimeOffset?) available
- `ICredentialService.cs` — HasValidToken() available
- `App.xaml.cs` — DI registration confirms all services available to SettingsViewModel
- macOS spec FEAT-03a/b/c/d/e/f — complete tab structure, badge specs, layout rules, time notation rules

### Secondary (MEDIUM confidence)
- WebSearch → NuGet packages list — confirmed SwitchPresenter is in CommunityToolkit.WinUI.Controls.Primitives (not installed)

### Tertiary (LOW confidence)
- WinUI3Localizer tree crawling of SegmentedItem.Content — behavior not verified with docs; flagged as Open Question 1

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all packages already installed, verified from .csproj
- Architecture: HIGH — Segmented API verified from official docs; Visibility pattern established in project
- Pitfalls: HIGH — derived from direct code inspection of existing XAML + ViewModel + spec
- Localization keys: HIGH — derived from existing .resw files and spec requirements

**Research date:** 2026-04-13
**Valid until:** 2026-05-13 (CommunityToolkit.WinUI 8.x — stable)
