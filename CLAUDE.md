# CCInfoWindows

Modified Windows port of [stefanlange/ccInfo](https://github.com/stefanlange/ccInfo).
Real-time Claude Code usage monitoring: 5-hour window, weekly quota, context window, token counts, cost analytics.

## Workflows

Two workflows coexist. Pick by task size:

| Task | Use |
|---|---|
| New milestone, multi-phase feature | **GSD** — `/gsd-progress`, `/gsd-new-milestone` |
| Single feature, refactor, multi-file change | **plan mode** (Shift+Tab) or `ultracode` |
| One-line fix, typo, obvious bugfix | just edit; conventional-commit it |

GSD is installed globally (`~/.claude/get-shit-done/`), not in this repo.
`ultracode` is session-only — type it in a prompt or `/effort ultracode`.
It cannot be persisted in settings.json.

**Where docs live** (tracked in git):

- `.planning/BACKLOG.md` — pending work, not yet in a milestone
- `.planning/MILESTONES.md` — changelog (per-milestone, shipped)
- `.planning/STATE.md` — GSD position; stale if last work was non-GSD
- `.planning/milestones/` — archived per-phase PLAN/SUMMARY/VERIFICATION
- `.planning/research/` — root-cause investigations
- `spec/` — upstream macOS parity specs (gitignored, reference only)

Plan-mode plans go to `~/.claude/plans/` (outside the repo, ephemeral).
Promote anything worth keeping into `.planning/` by hand.

**Global GSD hooks** fire in every session regardless of workflow. All
edit/commit guards are opt-in via `.planning/config.json` `hooks.*` and are
currently **off** — they advise, never block. Don't enable `hooks.community`
or `hooks.workflow_guard` unless you want GSD enforcement everywhere.

## MVVM Conventions

- Use `[ObservableProperty]` for bindable properties (generates PascalCase property from `_camelCase` field)
- Use `[RelayCommand]` for commands (generates `XxxCommand` from `Xxx` method)
- No code-behind logic in Views -- all logic in ViewModels
- Use `partial class` with source generators
- **G-1 (Messenger receive thread-marshaling)** — Every `IRecipient<T>.Receive(T)` method body that mutates `[ObservableProperty]` fields, calls `INavigationService`, or touches XAML controls MUST wrap the body in `IDispatcherQueue.TryEnqueue(() => HandleCore(...))`. Always-TryEnqueue is the rule — NEVER use the `if (!HasThreadAccess) ... else ...` shortcut, because recursive `Send → Receive` chains on the UI thread execute synchronously inside the parent's stack frame and produce mid-update inconsistent state. **Exception:** mark a method `[ThreadSafeReceive("specific reason proving UI-thread-only")]` and supply a non-empty reason — `MessengerThreadingConventionTests` enforces both branches. Window subclasses are exempt from the body-scan rule (they are by-construction UI-thread-bound) but MUST still carry `[ThreadSafeReceive(reason)]` to document the exemption. **Cross-VM communication priority:** direct DI > singleton-service .NET event > `WeakReferenceMessenger`. Reason: D-13 hotfix lesson — `WeakReferenceMessenger` + `AddTransient` recipients silently GC-drop, breaking exactly-once flows like logout / save-on-close.
- **G-3 (`[ObservableProperty]` default value rule — PREFERRED, not enforced)** — Prefer `= string.Empty;`, `= "--";`, or `= ParseHexBrush(...)` initializers over `null!` for `[ObservableProperty]` fields. `null!` defers a NullReferenceException to first read, which can fire from any binding evaluation site without clear stack trace context. Real defaults are testable, predictable, and preserve the visible behavior even before async initialization completes. When the field type requires WinRT COM activation (e.g., `SolidColorBrush`), inject a `Func<..., T>? brushFactory = null` testability seam and initialize in the constructor body. Precedent: `MainViewModel._contextModelBadgeColor` (Phase 28 CLEANUP-02 fix).

## Async Patterns

- Always `async/await` -- never fire-and-forget
- Use `DispatcherQueue.TryEnqueue()` for UI thread marshaling
- `HttpClient` as singleton (registered in DI)

## Naming Conventions

- PascalCase: public properties, methods, classes
- _camelCase: private fields
- I-prefix: interfaces (e.g., `INavigationService`)
- Conventional Commits: `feat:`, `fix:`, `chore:`, `docs:`, `refactor:`, `test:`

## Build Commands

```bash
# Run in debug mode -- shorthand from the repo root, any shell
.\dev

# Debug build (default)
dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj

# Run in debug mode (what dev.cmd wraps)
dotnet run --project CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj

# Release build (for desktop shortcut / taskbar pin)
dotnet build CCInfoWindows/CCInfoWindows/CCInfoWindows.csproj -c Release -o CCInfoWindows/CCInfoWindows/bin/x64/Release/net9.0-windows10.0.19041.0/
```

### Release Build Rules (STRICT)

- **NEVER use `dotnet publish` with trimming** -- `PublishTrimmed=true` breaks the app at runtime because System.Text.Json uses Reflection (IL2026). The trimmer removes types needed for JSON deserialization of API responses, settings, and cache files.
- **Always use `dotnet build -c Release`** instead of `dotnet publish` -- produces a working exe without trimming issues.
- **Always pass `-o`** to target the correct output directory -- without `-o`, the build outputs to a `win-x64/` subdirectory that differs from the expected launch path.
- **Release exe location:** `CCInfoWindows/CCInfoWindows/bin/x64/Release/net9.0-windows10.0.19041.0/CCInfoWindows.exe`
- **x64 only** -- both projects declare `<Platforms>x64</Platforms>` / `<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>` and the solution offers only `Any CPU` (redirects to x64) and `x64`. ARM64 was declared for a year without ever being built or tested; don't re-add it without an ARM64 device, an ARM64 installer, and a test run.
- **The rule is enforced, not just written down** -- `CCInfoWindows.csproj` pins `PublishTrimmed`/`PublishAot` to `false` and the `FailOnTrimmedPublish` target errors out if a `-p:` switch re-enables either. Fix the build command, never the guard.
- **The installer reads that same directory** -- `installer/setup.iss` packages it (excluding `win-x64\` and `*.pdb`), derives its version from the built `CCInfoWindows.exe`, and refuses to compile if the Release build was not run. Build order: `dotnet build -c Release -o ...` then `iscc installer/setup.iss`.
- **Version bump before tagging** -- `<Version>`/`<AssemblyVersion>`/`<FileVersion>` in the csproj plus the `README.md` version line. `UpdateService` compares the GitHub tag against the assembly version, so a stale assembly version means a permanent update banner.

## Security Rules

- **No secrets in source code** -- zero hardcoded tokens, keys, or passwords
- **Credential Manager only** -- all tokens stored via `AdysTech.CredentialManager` (DPAPI-encrypted)
- **WebView2 UDF isolation** -- User Data Folder at `%LOCALAPPDATA%\CCInfoWindows\WebView2`
- **Uninstall purges local state** -- `installer/setup.iss` deletes `%LOCALAPPDATA%\CCInfoWindows` and both Credential Manager targets. Never persist sensitive data anywhere else, or uninstall will leave it behind.
- **.gitignore enforced** -- settings.json, WebView2/, *.pfx, *.snk, .env excluded
- **Network calls only to** (HTTPS, complete list) -- `claude.ai` (usage API + login inside WebView2, which additionally loads whatever subresources that page references), `api.github.com` (`UpdateService` release check), `raw.githubusercontent.com` (`LiteLLMPricingService` price list), `github.com` (release page and upstream credits link, handed to the default browser via `Process.Start`)

## Diagnostics Channel

- **Handled failures go to `AppLog`** -- `AppLog.Write(source, ex)` / `AppLog.Write(source, message)` appends to `%LOCALAPPDATA%\CCInfoWindows\app.log` (1 MiB, single roll to `app.log.1`). `source` is a short call-site tag like `"MainView.OnLoaded"`. It never throws, is thread-safe, and works before the DI container exists. Every `catch` that degrades instead of rethrowing MUST call it with the exception -- a bare `catch { }` is a bug.
- **`Debug.WriteLine` is not a diagnostic channel** -- it carries `[Conditional("DEBUG")]`, so the compiler erases it from the Release build the users run. A catch body whose only statement is `Debug.WriteLine` is an empty catch body in production.
- **Unhandled exceptions** keep going to `crash.log` via `App.OnUnhandledException`, which also mirrors them into `app.log`. `AppPaths` owns both paths -- never rebuild `%LOCALAPPDATA%\CCInfoWindows` by hand.
- **Never pass a token or raw credential to the log** -- the sink's `sk-ant-*` redaction is defence in depth, not a licence.

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
