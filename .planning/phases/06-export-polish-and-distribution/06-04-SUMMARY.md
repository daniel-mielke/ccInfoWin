---
phase: 06-export-polish-and-distribution
plan: 04
subsystem: infra
tags: [dotnet-publish, inno-setup, installer, readme, license, distribution]

# Dependency graph
requires:
  - phase: 06-export-polish-and-distribution
    provides: feature-complete app with all UI, export, update, and localization functionality
provides:
  - Self-contained win-x64 publish with partial trimming configured in CCInfoWindows.csproj
  - Inno Setup script (installer/setup.iss) for per-user installer with desktop shortcut and autostart
  - README.md with features, installation, tech stack, and build instructions
  - MIT LICENSE file
affects:
  - GitHub release workflow
  - End-user installation

# Tech tracking
tech-stack:
  added: []
  patterns:
    - dotnet publish Release PropertyGroup with PublishTrimmed=true, TrimMode=partial, SelfContained=true
    - Inno Setup PrivilegesRequired=lowest for per-user installation without admin
    - HKCU Run key autostart with quoted path to handle paths with spaces

key-files:
  created:
    - installer/setup.iss
    - README.md
    - LICENSE
  modified:
    - CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj

key-decisions:
  - "Publish output path is bin/x64/Release/... (not bin/Release/...) due to Platforms=x64;ARM64 in .csproj — updated setup.iss source path accordingly"
  - "TrimMode=partial chosen over full link trimming to preserve WinRT interop and Win2D marshaling code (per RESEARCH.md pitfall 5)"
  - "PrivilegesRequired=lowest for per-user install — no UAC prompt, installs to LOCALAPPDATA\Programs\CCInfoWindows"
  - "Autostart ValueData uses triple-quoted path (\"\"\"...\"\"\") to handle paths with spaces in LOCALAPPDATA"

patterns-established:
  - "Version metadata (Version, AssemblyVersion, FileVersion, Product, Description, Copyright) in base PropertyGroup"
  - "Release-only publish properties isolated in Condition='Release' PropertyGroup to avoid affecting debug builds"

requirements-completed:
  - DIST-01
  - DIST-02
  - DIST-03

# Metrics
duration: 15min
completed: 2026-03-17
---

# Phase 6 Plan 04: Distribution Package Summary

**Self-contained win-x64 publish with partial trimming, Inno Setup per-user installer script (lzma2/ultra), MIT LICENSE, and GitHub-ready README.md**

## Performance

- **Duration:** ~15 min
- **Started:** 2026-03-17T14:38:00Z
- **Completed:** 2026-03-17T14:53:00Z
- **Tasks:** 2 of 3 auto tasks complete (Task 3 is human-verify checkpoint)
- **Files modified:** 4

## Accomplishments

- Added version metadata (1.0.0) and Release publish configuration to CCInfoWindows.csproj — `dotnet publish` produces self-contained win-x64 output
- Created `installer/setup.iss` with all required distribution settings: per-user install, desktop shortcut, autostart via HKCU Run key with quoted path, lzma2/ultra compression
- Created README.md with features, installation guide (installer + build from source), tech stack, and credits
- Created MIT LICENSE file with 2026 copyright

## Task Commits

Each task was committed atomically:

1. **Task 1: Configure publish profile and create Inno Setup installer script** - `b8bd81f` (feat)
2. **Task 2: Create README.md and LICENSE for GitHub repository** - `29b4ab8` (feat)

## Files Created/Modified

- `CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj` - Added version metadata (1.0.0) and Release PropertyGroup with PublishTrimmed/TrimMode/SelfContained
- `installer/setup.iss` - Inno Setup 6 script: per-user install, PrivilegesRequired=lowest, HKCU autostart, lzma2/ultra, German+English languages
- `README.md` - GitHub project README with features, installation, tech stack, build instructions, credits
- `LICENSE` - MIT License with 2026 Daniel Mielke

## Decisions Made

- **Publish output path correction:** The .csproj uses `<Platforms>x64;ARM64</Platforms>` which causes publish output to `bin/x64/Release/...` rather than `bin/Release/...`. The `setup.iss` [Files] source path was updated to match the actual output location.
- **TrimMode=partial:** Per RESEARCH.md pitfall 5, full trimming (`link`) breaks WinRT interop and Win2D marshaling. Partial trimming preserves app code and WinRT glue while still reducing size.
- **Triple-quoted autostart path:** `ValueData: """{app}\{#MyAppExeName}"""` ensures the executable path is quoted even when LOCALAPPDATA contains spaces (e.g., user profile with spaces in name).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Corrected publish output path in setup.iss**
- **Found during:** Task 1 (after running dotnet publish verification)
- **Issue:** Plan specified `bin\Release\...` but actual output path is `bin\x64\Release\...` due to `<Platforms>x64;ARM64</Platforms>` in .csproj
- **Fix:** Updated [Files] Source in setup.iss from `..\CCInfoWindows\CCInfoWindows\bin\Release\...` to `..\CCInfoWindows\CCInfoWindows\bin\x64\Release\...`
- **Files modified:** installer/setup.iss
- **Verification:** Path matches actual dotnet publish output confirmed with ls
- **Committed in:** b8bd81f (Task 1 commit)

---

**Total deviations:** 1 auto-fixed (1 bug — wrong publish output path)
**Impact on plan:** Essential correction. Without this fix the Inno Setup script would fail to find files during compilation.

## Issues Encountered

- `dotnet build` verification for Task 2 failed because the app was already running (file in use by process). This is an environment condition, not a code issue. The `dotnet publish` command from Task 1 verified the build correctly and succeeded without error. README.md and LICENSE are documentation files that do not affect build output.

## Next Phase Readiness

- All distribution artifacts are ready: self-contained publish configured, installer script ready for `iscc` compilation, README and LICENSE ready for GitHub
- Human verification required (Task 3 checkpoint): run dotnet publish, optionally compile and test installer with Inno Setup, review README and LICENSE
- After verification: repository can be pushed to GitHub and first release tagged as v1.0.0

---
*Phase: 06-export-polish-and-distribution*
*Completed: 2026-03-17*
