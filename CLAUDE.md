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

## Bash Permission Rules

- **Never chain commands** with `;`, `&&`, or `|` in a single Bash call -- Claude Code's permission system blocks compound commands even if each part is individually allowed
- **Use separate Bash tool calls** for each command instead (parallel if independent, sequential if dependent)
- Example: to kill the app and rebuild, use two separate Bash calls: `taskkill //F //IM CCInfoWindows.exe` then `dotnet build ...`

## Security Rules

- **No secrets in source code** -- zero hardcoded tokens, keys, or passwords
- **Credential Manager only** -- all tokens stored via `AdysTech.CredentialManager` (DPAPI-encrypted)
- **WebView2 UDF isolation** -- User Data Folder at `%LOCALAPPDATA%\CCInfoWindows\WebView2`
- **.gitignore enforced** -- settings.json, WebView2/, *.pfx, *.snk, .env excluded
- **Network calls only to** -- `claude.ai` and `raw.githubusercontent.com` (HTTPS)

## Clean Code Rules (authoritative)

Based on Robert C. Martin's Clean Code principles. All generated code MUST follow these rules:

- **No magic numbers** -- extract hard-coded values into named constants with meaningful names
- **Meaningful names** -- variables, methods, classes must reveal intent; if a name needs a comment, rename it
- **Small functions (SRP)** -- each function does one thing well; break large functions into smaller, focused ones
- **DRY** -- never duplicate logic; reuse via methods, classes, or abstractions
- **Wrap external libraries** -- never embed third-party API calls directly in business logic; use wrapper classes so libraries can be swapped without refactoring consumers
- **Minimal comments** -- code should be self-documenting; only comment unusual behavior or non-obvious "why"; never comment obvious things
- **Delete commented-out code** -- Git preserves history; commented-out code is noise, just delete it
- **F.I.R.S.T. tests** -- Fast, Independent, Repeatable, Self-Validating, Timely

## Secure Coding Rules (authoritative, OWASP-based)

Filtered for desktop/WinUI 3 context. All generated code MUST follow these rules:

### Credential & Data Protection
- **No secrets in code** -- zero hardcoded tokens, passwords, connection strings; use Credential Manager (DPAPI)
- **Encrypt sensitive stored data** -- authentication tokens, session data must be encrypted at rest
- **Purge temp data** -- remove cached/temporary copies of sensitive data as soon as no longer needed
- **Least privilege** -- restrict access to minimum necessary data and functionality

### Input Validation
- **Validate all external data** -- classify sources as trusted/untrusted; validate everything from untrusted sources (API responses, user input, file content)
- **Allow-list over deny-list** -- validate expected data types, ranges, lengths using allow-lists
- **Reject invalid input** -- all validation failures must result in rejection, never silent acceptance

### Error Handling & Logging
- **No sensitive data in errors** -- error messages must not expose tokens, system details, or stack traces to UI
- **Fail securely** -- security controls deny access by default on failure
- **No sensitive data in logs** -- never log tokens, session keys, or passwords
- **Generic error messages** -- show user-friendly messages; log technical details separately

### Session & Authentication
- **Logout must fully terminate** -- clear session tokens, cookies, and cached credentials
- **Session timeout** -- enforce inactivity timeout appropriate for the app context
- **Re-authenticate for sensitive ops** -- require fresh authentication before critical operations

### Communication Security
- **TLS only** -- all network communication over HTTPS; never fall back to HTTP
- **Validate TLS certificates** -- reject expired or invalid certificates

### General Coding Practices
- **Use managed code** -- prefer tested .NET APIs over unmanaged/P-Invoke for common tasks
- **Explicitly initialize variables** -- never rely on default values
- **No dynamic execution of user data** -- never pass external input to `Process.Start`, `ExecuteScriptAsync` with unescaped user content, or similar
- **Protect shared resources** -- use locking/synchronization to prevent race conditions on concurrent access
- **Dispose resources explicitly** -- use `using` statements; don't rely on GC for IDisposable objects
- **Minimize privilege elevation** -- if elevated privileges needed, acquire late and release early

### File Management
- **Validate file types by content** -- check file headers, not just extensions
- **Restrict file paths** -- never pass user-supplied paths directly; use allow-lists or index mappings
- **Read-only resources** -- application files and bundled resources should be read-only

## Reference Documents

- `spec/v1.7.1/ccinfo-spec.md` -- Functional requirements (10 areas, 40+ FA-IDs)
- `spec/v1.7.1/ccinfo-tech-spec.md` -- Technical specification (architecture, components, data flow)
- `spec/v1.7.1/ccinfo-styleguide.md` -- Pixel-precise design guide (colors #0F172A/#F1F5F9, typography, layout, spacing)
- `.claude/DOS-Secure-Coding.pdf` -- Full secure coding reference (OWASP)
- `.claude/DOS-Clean-code.pdf` -- Full clean code reference (Robert C. Martin)
