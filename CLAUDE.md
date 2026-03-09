# CCInfoWindows

Modified Windows port of [stefanlange/ccInfo](https://github.com/stefanlange/ccInfo) (macOS v1.7.1).
Real-time Claude Code usage monitoring: 5-hour window, weekly quota, context window, token counts, cost analytics.

## Stack

- **Language:** C# 13 / .NET 9
- **UI Framework:** WinUI 3 (Windows App SDK 1.8)
- **MVVM:** CommunityToolkit.Mvvm 8.4 (source generators)
- **DI:** Microsoft.Extensions.DependencyInjection
- **Credentials:** AdysTech.CredentialManager 3.1 (Win32 Credential Manager / DPAPI)
- **Charts:** Win2D (future phase)
- **Web:** WebView2 (embedded in WinUI 3)

## MVVM Conventions

- Use `[ObservableProperty]` for bindable properties (generates PascalCase property from `_camelCase` field)
- Use `[RelayCommand]` for commands (generates `XxxCommand` from `Xxx` method)
- No code-behind logic in Views -- all logic in ViewModels
- Use `partial class` with source generators

## Async Patterns

- Always `async/await` -- never fire-and-forget
- Use `DispatcherQueue.TryEnqueue()` for UI thread marshaling
- `HttpClient` as singleton (registered in DI)

## Naming Conventions

- PascalCase: public properties, methods, classes
- _camelCase: private fields
- I-prefix: interfaces (e.g., `INavigationService`)
- Conventional Commits: `feat:`, `fix:`, `chore:`, `docs:`, `refactor:`, `test:`

## Project Structure

```
CCInfoWindows/CCInfoWindows/
  Models/          -- Plain data objects (AppSettings, UsageData, etc.)
  ViewModels/      -- Observable state + commands
  Views/           -- XAML pages (LoginView, MainView, SettingsView)
  Services/        -- Business logic + I/O
    Interfaces/    -- Service contracts
  Messages/        -- CommunityToolkit.Mvvm messenger message types
  Helpers/         -- Pure utility functions
  Converters/      -- XAML value converters
  Assets/          -- Static resources (icons, images)
```

## Build Commands

```bash
dotnet build CCInfoWindows/CCInfoWindows.csproj
dotnet run --project CCInfoWindows/CCInfoWindows
dotnet publish CCInfoWindows/CCInfoWindows.csproj -c Release -r win-x64 --self-contained
```

## Security Rules

- **No secrets in source code** -- zero hardcoded tokens, keys, or passwords
- **Credential Manager only** -- all tokens stored via `AdysTech.CredentialManager` (DPAPI-encrypted)
- **WebView2 UDF isolation** -- User Data Folder at `%LOCALAPPDATA%\CCInfoWindows\WebView2`
- **.gitignore enforced** -- settings.json, WebView2/, *.pfx, *.snk, .env excluded
- **Network calls only to** -- `claude.ai` and `raw.githubusercontent.com` (HTTPS)

## Reference Documents

- `ccinfo-spec.md` -- Functional requirements (10 areas, 40+ FA-IDs)
- `ccinfo-tech-spec.md` -- Technical specification (architecture, components, data flow)
- `ccinfo-styleguide.md` -- Pixel-precise design guide (colors #0F172A/#F1F5F9, typography, layout, spacing)
- `.claude/DOS-Secure-Coding.pdf` -- Secure coding guidelines (authoritative)
- `.claude/DOS-Clean-code.pdf` -- Clean code guidelines (authoritative)
