# CCInfoWindows

Real-time Claude Code usage monitoring for Windows. A Windows port of [ccInfo](https://github.com/stefanlange/ccInfo) (macOS).

## Features

- **5-Hour Usage Window** — Interactive area chart with color-coded zones (green/yellow/orange/red), reset countdown, and glow indicator
- **Weekly Quota** — Separate Sonnet and Opus progress bars with reset dates
- **Context Window** — Real-time context utilization with model badge and autocompact warning
- **Multi-Session Management** — Switch between active Claude Code sessions with project names
- **Token Statistics** — Input/output/cache tokens aggregated by session, today, week, month
- **Cost Analytics** — Live pricing from LiteLLM, tiered pricing for 1M-context models, cost per time period
- **Chart Export** — Save 5-hour chart as PNG or copy to clipboard
- **Auto-Update** — Hourly version check with in-app update banner
- **Localization** — German and English (follows system language or manual selection)
- **Autostart** — Optional launch at Windows login

## Screenshots

<img width="427" height="1228" alt="image" src="https://github.com/user-attachments/assets/2c49465e-ba32-4d69-856f-4ec2a88d7470" />


## Installation

### Installer (Recommended)

Download the latest installer from [Releases](https://github.com/daniel-mielke/ccInfoWin/releases).

The installer:
- Installs per-user (no admin required)
- Optionally creates a desktop shortcut
- Optionally enables autostart at Windows login

### Build from Source

**Prerequisites:**
- .NET 9 SDK
- Windows 10 (19041) or later

```bash
git clone https://github.com/daniel-mielke/ccInfoWin.git
cd ccInfoWin
dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj
dotnet run --project CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj
```

**Release build:**
```bash
dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj -c Release -o CCInfoWindows/CCInfoWindows/bin/x64/Release/net9.0-windows10.0.19041.0/
```

> **Note:** Do not use `dotnet publish` with trimming — `PublishTrimmed` breaks JSON deserialization at runtime. Always use `dotnet build -c Release` instead.

## Tech Stack

- C# 13 / .NET 9
- WinUI 3 (Windows App SDK 1.8)
- CommunityToolkit.Mvvm 8.4
- Win2D 1.3.2 (charts)
- WebView2 (Cloudflare bypass)
- WinUI3Localizer 2.3.0 (localization)

## How It Works

CCInfoWindows monitors your Claude Code usage through two data paths:

1. **API Polling** — Authenticates via WebView2 cookie sharing, polls claude.ai for 5-hour window and weekly quota data
2. **JSONL File Watching** — Reads local `~/.claude/projects/` JSONL files for session context, token counts, and cost data

No telemetry. No tracking. All data stays local.

## License

[MIT](LICENSE)

## Credits

Based on [ccInfo](https://github.com/stefanlange/ccInfo) by Stefan Lange (macOS).
