# Phase 9: Layout & Structure - Research

**Researched:** 2026-03-19
**Domain:** WinUI 3 XAML layout restructuring (Grid, ScrollViewer, StackPanel, Border separators, .resw localization)
**Confidence:** HIGH

## Summary

This phase is pure XAML structural rearrangement with two localization file edits. No C# code changes are required anywhere. The existing XAML already contains all reusable primitives: the `<Border Height="1" Background="{ThemeResource DividerBrush}" />` separator pattern is used four times and simply needs to be placed at new positions. All section headers follow a well-established attribute pattern that the new "Active Session" header can copy verbatim.

The most architecturally significant change is moving the footer `<StackPanel>` from `Grid.Row="2"` (fixed/sticky) into the `ScrollViewer`'s inner `StackPanel`, which simultaneously satisfies LAYOUT-05 and allows the main `Grid.RowDefinitions` to collapse from `"Auto,*,Auto"` to `"Auto,*"`. The other structural change is relocating the entire Context Window block (currently lines 309–409) from after the Weekly sections to immediately after the new Active Session separator.

The statistics grid separator (LAYOUT-06) requires inserting a new `RowDefinition` as `Grid.Row="1"` and incrementing all subsequent `Grid.Row` attribute values by one (+1 on rows 1–7, giving rows 2–8). This is the only change that touches multiple XAML elements in a coordinated way and is therefore the highest-risk edit.

**Primary recommendation:** Execute changes as five discrete, focused edits: (1) padding, (2) Active Session header + separator, (3) Context Window block relocation, (4) footer relocation + Grid row collapse, (5) statistics grid separator insertion.

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

#### Section Order
- New order: Active Session header + Dropdown → [separator] → Context Window → [separator] → 5-Hour Window → Weekly → Sonnet Weekly → [separator] → Statistics → [separator] → Footer
- Context Window moves UP — currently it appears after Weekly sections, must appear before 5-Hour Window
- Footer moves INTO the ScrollViewer StackPanel (currently in Grid Row="2", fixed)

#### Header Styling
- "Active Session" header uses the same style as other section headers: FontSize="11", FontWeight="SemiBold", Foreground="{ThemeResource SectionHeaderBrush}", CharacterSpacing="50"
- Localized via l:Uids.Uid (new key "SectionHeaderActiveSession")

#### Separators
- Use existing `<Border Height="1" Background="{ThemeResource DividerBrush}" />` pattern
- New separators needed:
  1. Below Active Session dropdown (before Context Window)
  2. Above footer (inside ScrollViewer, after Statistics section)
  The existing separator between Context Window and Statistics (currently "Divider before STATISTIKEN") stays

#### Footer Behavior
- Footer StackPanel moves from Grid Row="2" into the ScrollViewer's StackPanel as the last child
- Add a separator Border above the footer buttons
- Remove Grid Row="2" from the main Grid (collapse to 2-row Grid: Auto,*)
- Padding on the footer StackPanel stays as-is (0,8,0,4)

#### Statistics Separator (LAYOUT-06)
- A separator between Row 0 (Modelle) and Row 1 (Eingabe) in the statistics Grid
- Insert as a new row in the Grid between existing Row 0 and Row 1
- All subsequent row indices shift by +1

#### Padding (LAYOUT-01)
- Main Grid currently has Padding="16,12,16,12" — vertical padding (12) is less than horizontal (16)
- Change to Padding="16,16,16,16" to equalize all sides

### Claude's Discretion
- Exact separator margin values: use Margin="0,4,0,0" above footer separator to give breathing room
- Statistics grid row separator: use Margin="0,2,0,2" consistent with existing Row 5 divider style

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope.
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| LAYOUT-01 | User sees vertical padding of the main app matching the horizontal padding (equal spacing on all sides) | Single attribute change: `Padding="16,12,16,12"` → `Padding="16,16,16,16"` on the root Grid (line 30) |
| LAYOUT-02 | User sees a localized "Active Session" / "Aktive Sitzung" label above the project dropdown, styled like other section headers | New `<TextBlock l:Uids.Uid="SectionHeaderActiveSession" .../>` inserted before the ComboBox block (line 82). New key added to both .resw files. |
| LAYOUT-03 | User sees a horizontal separator below the project dropdown visually separating it from the next section | New `<Border Height="1" Background="{ThemeResource DividerBrush}" />` inserted after the ComboBox and scanning indicators, before the Context Window section |
| LAYOUT-04 | User sees the "Context Window" section (including sub-agent row) positioned between "Active Session" and "5-Hour Window", with separators above and below | Move lines 309–409 (Divider + KONTEXTFENSTER StackPanel) to appear after the LAYOUT-03 separator, before the 5-Hour Window StackPanel. The separator currently at line 310 ("Divider before KONTEXTFENSTER") becomes the "separator below" LAYOUT-04 requires, leaving a second separator (the moved one from line 309) as the "separator above". |
| LAYOUT-05 | User can scroll to reach the footer (footer is no longer fixed/sticky), with a horizontal separator above it | Move Grid Row="2" StackPanel (lines 564–609) into ScrollViewer's inner StackPanel as last child; add `<Border Height="1" Background="{ThemeResource DividerBrush}" Margin="0,4,0,0" />` above it; collapse Grid RowDefinitions from `"Auto,*,Auto"` to `"Auto,*"` |
| LAYOUT-06 | User sees a horizontal separator between the "Models" row and the "Input" row in the Statistics section | Insert new RowDefinition as Grid.Row="1"; place `<Border Grid.Row="1" Grid.ColumnSpan="2" Height="1" Margin="0,2,0,2" Background="{ThemeResource DividerBrush}" />`; increment Grid.Row on all elements currently in rows 1–7 to rows 2–8 |
</phase_requirements>

---

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| WinUI 3 / Windows App SDK | 1.8 | XAML layout engine (Grid, StackPanel, ScrollViewer, Border) | Already in project — only file changed is MainView.xaml |
| WinUI3Localizer | existing | `l:Uids.Uid` runtime localization from .resw files | Established pattern; all section headers already use it |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| .resw resource files | — | Key/value localization strings | Required for every new localizable string |

**Installation:** No new packages required.

---

## Architecture Patterns

### Current XAML Structure (before phase)

```
Grid (RowDefinitions="Auto,*,Auto" Padding="16,12,16,12")
  ├── Row 0: StackPanel (InfoBars)
  ├── Row 1: ScrollViewer
  │     └── StackPanel Spacing="16"
  │           ├── ComboBox (session dropdown)          ← lines 82–98
  │           ├── TextBlock (scanning indicator)       ← lines 101–105
  │           ├── TextBlock (updating indicator)       ← lines 108–113
  │           ├── StackPanel (5-HOUR section)          ← lines 116–189
  │           ├── Border (divider)                     ← line 192
  │           ├── StackPanel (WEEKLY section)          ← lines 195–247
  │           ├── Border (divider, conditional)        ← lines 250–251
  │           ├── StackPanel (SONNET WEEKLY section)   ← lines 254–307
  │           ├── Border (divider before CONTEXT)      ← line 310
  │           ├── StackPanel (CONTEXT WINDOW section)  ← lines 312–409
  │           ├── Border (divider before STATS)        ← line 412
  │           └── StackPanel (STATS section)           ← lines 415–558
  └── Row 2: StackPanel (footer, fixed)                ← lines 564–609
```

### Target XAML Structure (after phase)

```
Grid (RowDefinitions="Auto,*" Padding="16,16,16,16")   ← LAYOUT-01
  ├── Row 0: StackPanel (InfoBars)
  └── Row 1: ScrollViewer
        └── StackPanel Spacing="16"
              ├── TextBlock (SectionHeaderActiveSession) ← LAYOUT-02 NEW
              ├── ComboBox (session dropdown)
              ├── TextBlock (scanning indicator)
              ├── TextBlock (updating indicator)
              ├── Border (divider)                       ← LAYOUT-03 NEW
              ├── StackPanel (CONTEXT WINDOW section)   ← LAYOUT-04 MOVED UP
              ├── Border (divider, existing "Divider before STATISTIKEN" keeps position logic but is now "below context")
              ├── StackPanel (5-HOUR section)
              ├── Border (divider)
              ├── StackPanel (WEEKLY section)
              ├── Border (divider, conditional)
              ├── StackPanel (SONNET WEEKLY section)
              ├── Border (divider before STATS, existing)
              ├── StackPanel (STATS section, with internal Row 0/1 separator)  ← LAYOUT-06
              ├── Border (divider above footer)          ← LAYOUT-05 NEW
              └── StackPanel (footer buttons)            ← LAYOUT-05 MOVED
```

### Pattern 1: Existing Separator Pattern
**What:** Single-pixel horizontal rule using a `Border` element
**When to use:** Between all major sections and before the footer
**Example:**
```xml
<!-- Source: existing MainView.xaml line 192 -->
<Border Height="1" Background="{ThemeResource DividerBrush}" />
```
For footer separator with breathing room (Claude's discretion):
```xml
<Border Height="1" Background="{ThemeResource DividerBrush}" Margin="0,4,0,0" />
```
For statistics internal separator (matching existing Row 5 style at line 530):
```xml
<Border Grid.Row="1" Grid.ColumnSpan="2" Height="1" Margin="0,2,0,2"
        Background="{ThemeResource DividerBrush}" />
```

### Pattern 2: Section Header TextBlock
**What:** Uppercase label above a section with specific font styling and localization
**When to use:** Above every major content group
**Example:**
```xml
<!-- Source: existing MainView.xaml lines 314–317 -->
<TextBlock l:Uids.Uid="SectionHeaderActiveSession"
           FontSize="11" FontWeight="SemiBold"
           Foreground="{ThemeResource SectionHeaderBrush}"
           CharacterSpacing="50" />
```

### Pattern 3: .resw Localization Key
**What:** Key/value pair in Resources.resw for UI text
**When to use:** Any new displayed text string
**Example:**
```xml
<!-- Source: existing Strings/en-US/Resources.resw lines 21–35 pattern -->
<data name="SectionHeaderActiveSession.Text" xml:space="preserve">
  <value>ACTIVE SESSION</value>
</data>
```
```xml
<!-- Source: existing Strings/de-DE/Resources.resw same block -->
<data name="SectionHeaderActiveSession.Text" xml:space="preserve">
  <value>AKTIVE SITZUNG</value>
</data>
```
The `.Text` suffix is the WinUI3Localizer convention — it maps `Uid="SectionHeaderActiveSession"` to the `Text` property of the TextBlock.

### Pattern 4: Statistics Grid Row Index Shift
**What:** When inserting a new row into a Grid, all elements assigned to higher row indices need their `Grid.Row` value incremented
**When to use:** LAYOUT-06 — inserting separator row between Modelle (Row 0) and Eingabe (Row 1)
**Example (current → target):**

| Element | Current Grid.Row | After insert |
|---------|-----------------|--------------|
| StatsModels (label + value) | 0 | 0 (unchanged) |
| NEW separator | — | 1 |
| StatsInput (label + value) | 1 | 2 |
| StatsOutput | 2 | 3 |
| StatsCacheWrite | 3 | 4 |
| StatsCacheRead | 4 | 5 |
| Existing divider (Row 5) | 5 | 6 |
| StatsTotal | 6 | 7 |
| StatsCost | 7 | 8 |

A new `<RowDefinition Height="Auto" />` entry must also be added to `Grid.RowDefinitions` (total: 9 rows after insertion, up from 8).

### Anti-Patterns to Avoid
- **Editing `Grid.Row` on only some of the affected elements**: All elements at rows 1–7 must shift to 2–8 — missing even one will cause elements to stack visually.
- **Forgetting to add the new `<RowDefinition>`**: The statistics Grid RowDefinitions block (lines 457–466) must gain one new `<RowDefinition Height="Auto" />` entry, otherwise WinUI 3 will silently clip or misplace the extra row.
- **Leaving `Grid.Row="2"` on the footer StackPanel**: After moving the footer into the ScrollViewer, the attribute `Grid.Row="2"` must be removed (or the element will still try to reference a non-existent row).
- **Leaving `RowDefinitions="Auto,*,Auto"`** after footer moves: The third row definition becomes an empty row — collapse to `"Auto,*"`.
- **Wrapping the moved Context Window block in a new StackPanel**: It moves as-is; no wrapper changes.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Horizontal rule | Custom Canvas drawing | `<Border Height="1" Background="{ThemeResource DividerBrush}" />` | Already exists 4x in the file; consistent theme binding |
| Section header label | Custom UserControl | Inline `<TextBlock>` with known attributes | All existing headers are inline; no abstraction needed |

---

## Common Pitfalls

### Pitfall 1: Statistics Grid Row Shift Incompleteness
**What goes wrong:** One or more elements in the statistics Grid retain their old `Grid.Row` value after the separator row insertion, causing two elements to occupy the same row and visually overlap or collapse.
**Why it happens:** The statistics Grid has 8 rows × 2 columns = 16 elements plus 3 shimmer Border elements per data row — 24+ attributes to update. Easy to miss one.
**How to avoid:** After inserting the new RowDefinition, do a targeted find on `Grid.Row=` within the statistics Grid block and enumerate all values systematically from 1 onward.
**Warning signs:** Statistics section appears truncated or rows visually overlap during runtime.

### Pitfall 2: Footer Grid.Row Attribute Left Behind
**What goes wrong:** The footer StackPanel is moved into the ScrollViewer but the `Grid.Row="2"` attribute is not removed. WinUI 3 will throw a layout warning or silently ignore it since the parent is now a StackPanel, not a Grid — but it's dead code.
**How to avoid:** Delete `Grid.Row="2"` from the footer StackPanel opening tag when moving the element.
**Warning signs:** Compiler warning about Grid.Row on a non-Grid child (varies by WinUI 3 version).

### Pitfall 3: Context Window Block Move Breaks Existing "Divider before STATISTIKEN"
**What goes wrong:** The separator at line 412 ("Divider before STATISTIKEN") was positioned between Context Window and Statistics. After moving Context Window up, this separator still sits between wherever Context Window now ends and Statistics begins — which is correct. However, the separator at line 310 ("Divider before KONTEXTFENSTER") currently precedes the Context Window in its old location. When Context Window moves, that separator must also move with it (or be replaced).
**How to avoid:** Move the entire block from line 309 (the divider) through line 409 (end of Context Window StackPanel). The LAYOUT-03 separator (new, below dropdown) becomes the "above" separator for Context Window. The existing "Divider before STATISTIKEN" (line 412) becomes the "below" separator for Context Window.
**Warning signs:** Two separators appear adjacent with no content between them, or the Context Window section appears without a separator above it.

### Pitfall 4: StackPanel Spacing Creates Double Gap at Footer
**What goes wrong:** The `StackPanel Spacing="16"` on the main content container adds 16px above every child, including the footer StackPanel. The footer separator also has `Margin="0,4,0,0"`. The visual gap between Statistics and the separator may be larger than expected (16px from Spacing + 4px from Margin = 20px before the separator line itself).
**How to avoid:** This is expected and acceptable — the Spacing="16" gap is consistent with all other section spacing. The 4px Margin on the separator adds breathing room between the separator line and the footer buttons below.
**Warning signs:** None — this is by design per Claude's Discretion in CONTEXT.md.

---

## Code Examples

Verified patterns from existing MainView.xaml:

### New Active Session Header (LAYOUT-02)
```xml
<!-- Insert before line 82 (ComboBox SessionComboBox) -->
<TextBlock l:Uids.Uid="SectionHeaderActiveSession"
           FontSize="11" FontWeight="SemiBold"
           Foreground="{ThemeResource SectionHeaderBrush}"
           CharacterSpacing="50" />
```

### New Separator Below Dropdown (LAYOUT-03)
```xml
<!-- Insert after scanning/updating indicator TextBlocks, before Context Window section -->
<Border Height="1" Background="{ThemeResource DividerBrush}" />
```

### Footer Separator (LAYOUT-05)
```xml
<!-- Insert as second-to-last child in ScrollViewer StackPanel, above footer StackPanel -->
<Border Height="1" Background="{ThemeResource DividerBrush}" Margin="0,4,0,0" />
```

### Statistics Row 1 Separator (LAYOUT-06)
```xml
<!-- Insert after existing RowDefinitions as 2nd entry (index 1) -->
<RowDefinition Height="Auto" />   <!-- new row for separator -->

<!-- New Grid element at Row 1 -->
<Border Grid.Row="1" Grid.ColumnSpan="2" Height="1" Margin="0,2,0,2"
        Background="{ThemeResource DividerBrush}" />
```

### Localization Keys (both .resw files)
```xml
<!-- en-US/Resources.resw — insert within "MainView section headers" comment block -->
<data name="SectionHeaderActiveSession.Text" xml:space="preserve">
  <value>ACTIVE SESSION</value>
</data>
```
```xml
<!-- de-DE/Resources.resw — insert within "MainView section headers" comment block -->
<data name="SectionHeaderActiveSession.Text" xml:space="preserve">
  <value>AKTIVE SITZUNG</value>
</data>
```

### Root Grid Changes (LAYOUT-01 + LAYOUT-05)
```xml
<!-- Before -->
<Grid Background="{ThemeResource AppBackgroundBrush}"
      RowDefinitions="Auto,*,Auto"
      Padding="16,12,16,12">

<!-- After -->
<Grid Background="{ThemeResource AppBackgroundBrush}"
      RowDefinitions="Auto,*"
      Padding="16,16,16,16">
```

---

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xunit 2.9.3 |
| Config file | CCInfoWindows.Tests/CCInfoWindows.Tests.csproj |
| Quick run command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --arch x64` |
| Full suite command | `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --arch x64` |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| LAYOUT-01 | Padding attribute equals "16,16,16,16" | manual-only (XAML visual) | n/a — visual inspection | n/a |
| LAYOUT-02 | Active Session header appears above dropdown | manual-only (XAML visual) | n/a — visual inspection | n/a |
| LAYOUT-03 | Separator renders below dropdown | manual-only (XAML visual) | n/a — visual inspection | n/a |
| LAYOUT-04 | Context Window section appears before 5-Hour Window | manual-only (XAML visual) | n/a — visual inspection | n/a |
| LAYOUT-05 | Footer scrolls with content, not fixed | manual-only (XAML visual) | n/a — visual inspection | n/a |
| LAYOUT-06 | Separator between Models and Input rows | manual-only (XAML visual) | n/a — visual inspection | n/a |

All LAYOUT requirements are pure XAML structural changes with no C# logic. Automated unit tests cannot verify XAML layout rendering in WinUI 3 without a full UI automation harness (not present in this project). Verification is via visual inspection of the running app.

The existing test suite (xunit + Moq, Services/ViewModels tests) has no impact from this phase and should continue to pass unchanged.

### Sampling Rate
- **Per task commit:** `dotnet test CCInfoWindows.Tests/CCInfoWindows.Tests.csproj --arch x64` (ensures no regressions in non-XAML code)
- **Per wave merge:** same command
- **Phase gate:** Existing test suite green + visual inspection of running app confirms all 6 layout changes before `/gsd:verify-work`

### Wave 0 Gaps
None — existing test infrastructure covers all phase requirements (all are manual-only visual checks; no test file stubs needed).

---

## Sources

### Primary (HIGH confidence)
- Direct read of `CCInfoWindows/CCInfoWindows/Views/MainView.xaml` — exact line numbers, existing element structure, existing separator/header patterns
- Direct read of `CCInfoWindows/CCInfoWindows/Strings/en-US/Resources.resw` and `de-DE/Resources.resw` — exact key format, section header comment block location
- Direct read of `09-CONTEXT.md` — all locked implementation decisions

### Secondary (MEDIUM confidence)
- WinUI 3 Grid behavior for missing RowDefinition: confirmed by project's existing Grid usage patterns (statistics Grid at line 452 uses 8 RowDefinitions correctly)

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — no new libraries; all changes are to existing files using established patterns
- Architecture: HIGH — current XAML structure fully read and mapped; target structure derived directly from locked CONTEXT.md decisions
- Pitfalls: HIGH — identified from direct code analysis of the elements being moved/modified

**Research date:** 2026-03-19
**Valid until:** Until MainView.xaml is modified by another phase (next: Phase 10 Visual Styles touches AppTheme.xaml, not MainView structure)
