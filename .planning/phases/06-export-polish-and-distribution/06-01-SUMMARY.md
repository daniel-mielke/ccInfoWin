---
phase: 06-export-polish-and-distribution
plan: 01
status: complete
started: 2026-03-17
completed: 2026-03-17
---

## What Was Built

Backend services and helpers for chart export, GitHub update checking, and autostart management.

### Task 1: ExportHelper (Win2D offscreen PNG rendering)
- `ExportHelper.cs`: Static class with `RenderChartToPng`, `ExportChartAsPngAsync`, `CopyChartToClipboardAsync`
- Renders chart at 192 DPI (2x) producing 656x480 physical pixels
- Reuses `ChartRenderer` math and `ChartColors` for zone-colored fills, top lines, and glow indicators
- Composition: dark background, "5-STUNDEN-FENSTER" label, percentage/countdown text, chart area, "CCINFO" watermark
- `FileSavePicker` for save, `DataPackage` clipboard for copy
- Tests: 3 constant validation tests + 2 GPU-dependent render tests

### Task 2: UpdateService + RegistryHelper + AppSettings
- `IUpdateService.cs` / `UpdateService.cs`: Hourly GitHub Releases API check with SemVer comparison, `UpdateAvailable` event, dismissed version support
- `GitHubRelease.cs`: DTO for GitHub API response
- `RegistryHelper.cs`: HKCU Run key read/write for autostart
- `AppSettings.cs`: Extended with `DismissedUpdateVersion` and `Language` (default "de-DE")
- Tests: 6 UpdateService tests (parse, compare, deserialize) + 2 RegistryHelper tests

## Self-Check: PASSED

## Key Files

### key-files.created
- CCInfoWindows/CCInfoWindows/Helpers/ExportHelper.cs
- CCInfoWindows/CCInfoWindows/Services/UpdateService.cs
- CCInfoWindows/CCInfoWindows/Services/Interfaces/IUpdateService.cs
- CCInfoWindows/CCInfoWindows/Helpers/RegistryHelper.cs
- CCInfoWindows/CCInfoWindows/Models/GitHubRelease.cs
- CCInfoWindows.Tests/Helpers/ExportHelperTests.cs
- CCInfoWindows.Tests/Services/UpdateServiceTests.cs
- CCInfoWindows.Tests/Helpers/RegistryHelperTests.cs

### key-files.modified
- CCInfoWindows/CCInfoWindows/Models/AppSettings.cs

## Deviations

None.

## Test Results

- 11 non-GPU tests: all passed
- 2 GPU tests: require hardware (marked with `[Trait("Category", "RequiresGPU")]`)
