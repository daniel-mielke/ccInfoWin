# Phase 21: History Persistence Hardening - Pattern Map

**Mapped:** 2026-05-06
**Files analyzed:** 5 (1 interface modified, 1 service modified, 1 viewmodel modified, 1 window code-behind modified, 1 test file extended; optionally 1 new VM-test file)
**Analogs found:** 5 / 5 (100% — every needed pattern exists in the codebase)

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `CCInfoWindows/Services/Interfaces/IUsageHistoryService.cs` (modify) | service contract | file I/O | `Services/Interfaces/IClaudeApiService.cs` (sync `GetCachedUsage` + async `SaveCacheAsync`/`LoadCacheAsync` siblings) | exact (sync+async member coexistence on one interface) |
| `CCInfoWindows/Services/UsageHistoryService.cs` (modify) | service impl | file I/O + concurrency | self (sync `SaveHistory` already at lines 52-64) **AND** `Services/LiteLLMPricingService.cs:26,43-57` (`SemaphoreSlim` init + `WaitAsync`/`Release` in finally) **AND** `Services/ClaudeApiService.cs:121-131` (`File.WriteAllTextAsync` + `JsonSerializer.Serialize` shape) | exact (composite of three local analogs) |
| `CCInfoWindows/ViewModels/MainViewModel.cs` (modify) | viewmodel | request-response (poll) | self (`PollUsageAsync` already async at line 404; `_fiveHourResetsAt` precedent for tracked previous-poll state at line 100) | exact |
| `CCInfoWindows/MainWindow.xaml.cs` (modify) | view code-behind | termination hook | self (`OnClosing` handler already at lines 107-116, already wired at line 42, already does sync save via `_settingsService.SaveWindowState`) | exact (literal extension of existing handler) |
| `CCInfoWindows.Tests/Services/UsageHistoryServiceTests.cs` (extend) | unit test | file I/O round-trip | self (6 tests already there at lines 17-118; constructor at 11-15; `IDisposable.Dispose` at 120-126) | exact |
| `CCInfoWindows.Tests/ViewModels/MainViewModelHistoryTests.cs` (NEW, optional) | unit test | viewmodel + service mock | `Tests/ViewModels/MainViewModelAuthFlowTests.cs` (full-DI Moq factory at lines 18-47, includes `Mock<IUsageHistoryService>` at line 25) | exact |

---

## Pattern Assignments

### `Services/Interfaces/IUsageHistoryService.cs` (service contract, file I/O)

**Analog:** Itself + `Services/Interfaces/IClaudeApiService.cs`

**Current full file** (`Services/Interfaces/IUsageHistoryService.cs:1-13`):
```csharp
using CCInfoWindows.Models;

namespace CCInfoWindows.Services.Interfaces;

public interface IUsageHistoryService
{
    UsageHistory LoadHistory();
    void SaveHistory(UsageHistory history);
    void ClearHistory();
}
```

**Sync+async coexistence precedent on one interface** (`Services/Interfaces/IClaudeApiService.cs:10-16`):
```csharp
Task<UsageResponse?> FetchUsageAsync(CancellationToken ct = default);
// ...
Task SaveCacheAsync(UsageResponse data);
// ...
Task<UsageResponse?> LoadCacheAsync();
```
Note: `IClaudeApiService` mixes `Task`-returning async members with `UsageResponse? GetCachedUsage()` (sync getter) — exactly the shape Phase 21 needs (`Task SaveHistoryAsync(...)` async sibling + `UsageHistory? PeekLastSnapshot()` sync getter).

**Add (D-06, D-04):**
```csharp
Task SaveHistoryAsync(UsageHistory history);
UsageHistory? PeekLastSnapshot();
```
Naming convention: nullable-return for optional state matches the codebase (no `Try*(out ...)` precedent in service interfaces — `ICredentialService.HasValidToken()` returns plain `bool` separately from getters; planner picks `UsageHistory? PeekLastSnapshot()` per RESEARCH §Open Questions #2).

---

### `Services/UsageHistoryService.cs` (service impl, file I/O + concurrency)

**Analog:** Self (sync write at lines 52-64) + `LiteLLMPricingService.cs` (semaphore) + `ClaudeApiService.cs` (`WriteAllTextAsync`)

**Imports pattern** (`Services/UsageHistoryService.cs:1-3`):
```csharp
using System.Text.Json;
using CCInfoWindows.Models;
using CCInfoWindows.Services.Interfaces;
```
No additional usings needed — `SemaphoreSlim` lives in `System.Threading` which is in implicit usings.

**Existing `JsonSerializerOptions` singleton — reuse for byte-identical JSON (D-07)** (`Services/UsageHistoryService.cs:17-20`):
```csharp
private static readonly JsonSerializerOptions JsonOptions = new()
{
    WriteIndented = true
};
```
Both `SaveHistory` and `SaveHistoryAsync` MUST share this exact instance. No new options object.

**Existing sync save pattern to wrap with semaphore (D-05)** (`Services/UsageHistoryService.cs:52-64`):
```csharp
public void SaveHistory(UsageHistory history)
{
    try
    {
        Directory.CreateDirectory(_historyDirectory);
        var json = JsonSerializer.Serialize(history, JsonOptions);
        File.WriteAllText(HistoryFilePath, json);
    }
    catch
    {
        // Best-effort save -- don't crash the app
    }
}
```
After Phase 21: wrap body in `_writeLock.Wait()` / `try { ... } finally { _writeLock.Release(); }`. The existing `try/catch` (best-effort) MUST sit INSIDE the `try` so `finally` always releases (RESEARCH Pitfall 1).

**Existing clear pattern — must null snapshot BEFORE delete (D-13)** (`Services/UsageHistoryService.cs:66-76`):
```csharp
public void ClearHistory()
{
    try
    {
        File.Delete(HistoryFilePath);
    }
    catch
    {
        // No-op if file not found
    }
}
```
After Phase 21: enter semaphore, then `_lastSavedSnapshot = null;` THEN `File.Delete(HistoryFilePath);` (order matters — RESEARCH Pitfall 3).

**SemaphoreSlim init pattern — copy from `LiteLLMPricingService.cs:26`:**
```csharp
private readonly SemaphoreSlim _loadLock = new(1, 1);
```

**SemaphoreSlim async wait + finally release pattern** (`Services/LiteLLMPricingService.cs:43-57`):
```csharp
public async Task EnsurePricesLoadedAsync()
{
    await _loadLock.WaitAsync();
    try
    {
        if (_pricingMap.Count > 0 && !IsCacheExpired())
            return;

        await TryLoadFromLiveApi();
    }
    finally
    {
        _loadLock.Release();
    }
}
```
Phase 21's `SaveHistoryAsync` mirrors this shape verbatim — the only project precedent for `SemaphoreSlim` use, so the planner has a single canonical pattern.

**File.WriteAllTextAsync pattern** (`Services/ClaudeApiService.cs:121-131`):
```csharp
public async Task SaveCacheAsync(UsageResponse data)
{
    var dir = Path.GetDirectoryName(_cacheFilePath)!;
    Directory.CreateDirectory(dir);

    var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
    {
        WriteIndented = false
    });
    await File.WriteAllTextAsync(_cacheFilePath, json);
}
```
WARNING: `ClaudeApiService.SaveCacheAsync` constructs a per-call `JsonSerializerOptions` — Phase 21 MUST NOT copy that anti-pattern. Use the existing static `JsonOptions` field at `UsageHistoryService.cs:17-20` to satisfy D-07 byte-identical JSON.

**Composite Phase-21 shape (assembled from the three excerpts above):**
```csharp
private readonly SemaphoreSlim _writeLock = new(1, 1);
private UsageHistory? _lastSavedSnapshot;

public void SaveHistory(UsageHistory history)
{
    _writeLock.Wait();
    try
    {
        try
        {
            Directory.CreateDirectory(_historyDirectory);
            var json = JsonSerializer.Serialize(history, JsonOptions);
            File.WriteAllText(HistoryFilePath, json);
            _lastSavedSnapshot = history;   // AFTER successful write (RESEARCH Pitfall 2)
        }
        catch
        {
            // Best-effort save
        }
    }
    finally
    {
        _writeLock.Release();
    }
}

public async Task SaveHistoryAsync(UsageHistory history)
{
    await _writeLock.WaitAsync();
    try
    {
        try
        {
            Directory.CreateDirectory(_historyDirectory);
            var json = JsonSerializer.Serialize(history, JsonOptions);
            await File.WriteAllTextAsync(HistoryFilePath, json);
            _lastSavedSnapshot = history;
        }
        catch
        {
            // Best-effort save
        }
    }
    finally
    {
        _writeLock.Release();
    }
}

public UsageHistory? PeekLastSnapshot() => _lastSavedSnapshot;
```

---

### `ViewModels/MainViewModel.cs` (viewmodel, request-response)

**Analog:** Self — every needed pattern already exists in this file.

**Existing `_previous*-style` private field convention** (`ViewModels/MainViewModel.cs:100,127,146`):
```csharp
private DateTimeOffset? _fiveHourResetsAt;
// ...
private DateTimeOffset? _weeklyResetsAt;
// ...
private DateTimeOffset? _sonnetResetsAt;
```
No `_previous*` fields exist in MainViewModel; the project's convention for "previously observed reset timestamp" is `_<windowName>ResetsAt`. Phase 21 reuses these fields (already populated in `AppendHistoryPoint:557` and `ApplyWeeklyWindow:484/490`). HIST-04 reset detection consumes `history.ResetsAt` from disk via `_historyService.LoadHistory()` — no new tracking field needed (D-12).

**Existing async cascade entry point** (`ViewModels/MainViewModel.cs:404-441`, abridged):
```csharp
private async Task PollUsageAsync()
{
    if (IsRefreshing) return;
    IsRefreshing = true;
    HasApiError = false;
    ApiErrorMessage = string.Empty;

    try
    {
        var result = await _apiService.FetchUsageAsync();
        if (result != null)
        {
            UpdateUsageProperties(result);   // ← becomes await UpdateUsagePropertiesAsync(result)
            _autoReauthAttempted = false;
        }
        // ...
    }
    catch (HttpFetchException ex) { /* ... */ }
    catch (Exception ex) { /* ... */ }
    finally { IsRefreshing = false; }
}
```

**Existing sync `UpdateUsageProperties` to convert (D-08)** (`ViewModels/MainViewModel.cs:443-491`, abridged):
```csharp
private void UpdateUsageProperties(UsageResponse data)
{
    if (data.FiveHour != null)
    {
        // ...
        AppendHistoryPoint(data.FiveHour.ResetsAt, util);   // ← await AppendHistoryPointAsync(...)
        // ...
    }
    // ...
}
```
After Phase 21: signature becomes `private async Task UpdateUsagePropertiesAsync(UsageResponse data)`. Both call sites must `await` (`MainViewModel.cs:370` cache-load path inside `InitializeAsync`, and `MainViewModel.cs:416` poll path) — RESEARCH Pitfall 4.

**Existing sync `AppendHistoryPoint` to convert (D-08)** (`ViewModels/MainViewModel.cs:530-560`):
```csharp
private void AppendHistoryPoint(DateTimeOffset? apiResetsAt, double utilization)
{
    var history = _historyService.LoadHistory();

    var windowResetDetected = IsWindowReset(history.ResetsAt, apiResetsAt);

    if (windowResetDetected)
    {
        history = new UsageHistory();
    }

    history.ResetsAt = apiResetsAt;

    var now = DateTimeOffset.UtcNow;
    var windowDuration = TimeSpan.FromHours(5);
    var cutoff = now - windowDuration;
    history.Points.RemoveAll(p => p.Timestamp < cutoff);

    history.Points.Add(new UsageHistoryPoint
    {
        Timestamp = now,
        Utilization = utilization
    });

    _historyService.SaveHistory(history);   // ← await _historyService.SaveHistoryAsync(history);

    _fiveHourResetsAt = apiResetsAt;
    UsageHistoryPoints = history.Points.AsReadOnly();
    InvalidateChart();
}
```
Phase-21 signature: `private async Task AppendHistoryPointAsync(DateTimeOffset? apiResetsAt, double utilization)`. ONE line changes — `SaveHistory` → `await SaveHistoryAsync`.

**Existing `IsWindowReset` — DO NOT TOUCH (D-10/D-11)** (`ViewModels/MainViewModel.cs:562-570`):
```csharp
private static readonly TimeSpan WindowResetTolerance = TimeSpan.FromMinutes(2);

private static bool IsWindowReset(DateTimeOffset? storedResetsAt, DateTimeOffset? apiResetsAt)
{
    if (!storedResetsAt.HasValue || !apiResetsAt.HasValue) return false;

    var difference = (apiResetsAt.Value - storedResetsAt.Value).Duration();
    return difference > WindowResetTolerance;
}
```
This satisfies HIST-04 (tolerance-based reset) AND HIST-05 (null-previous guard at line 566). Phase 21 adds verification tests; ZERO code changes here.

**Existing `Logout` — keep `ClearHistory` call (D-13)** (`ViewModels/MainViewModel.cs:875-885`):
```csharp
[RelayCommand]
private void Logout()
{
    _historyService.ClearHistory();
    _credentialService.ClearCredentials();
    _bridge.Reset();
    WeakReferenceMessenger.Default.Send(new AuthStateChangedMessage(false));
    IsSessionExpired = false;
    _autoReauthAttempted = false;
    _navigationService.NavigateTo<LoginView>();
}
```
NO change here. The `ClearHistory()` call now also nulls `_lastSavedSnapshot` (handled inside the service — D-13 contract). The race-avoidance lives in the service, not the VM.

---

### `MainWindow.xaml.cs` (view code-behind, termination hook)

**Analog:** Self — handler already exists, only needs ~5 lines appended.

**Existing service-locator field+constructor pattern to copy (D-14)** (`MainWindow.xaml.cs:28-36`):
```csharp
private readonly ISettingsService _settingsService;
private readonly INavigationService _navigationService;

public MainWindow()
{
    InitializeComponent();

    _settingsService = App.Services.GetRequiredService<ISettingsService>();
    _navigationService = App.Services.GetRequiredService<INavigationService>();
    // ...
}
```
Phase 21 adds:
```csharp
private readonly IUsageHistoryService _historyService;
// in constructor:
_historyService = App.Services.GetRequiredService<IUsageHistoryService>();
```
RESEARCH Pitfall 6: stay on the constructor + `App.Services.GetRequiredService<T>()` pattern; do NOT inline `App.Services.GetRequiredService<T>()` inside `OnClosing`. Maintain consistency with existing `_settingsService`/`_navigationService` injection.

**Existing `AppWindow.Closing` wire-up — already in place** (`MainWindow.xaml.cs:42`):
```csharp
AppWindow.Closing += OnClosing;
```
ZERO change to this line.

**Existing `OnClosing` handler — append history-flush block (D-14)** (`MainWindow.xaml.cs:107-116`):
```csharp
private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
{
    var state = new WindowState(
        AppWindow.Position.X,
        AppWindow.Position.Y,
        AppWindow.Size.Width,
        AppWindow.Size.Height);

    _settingsService.SaveWindowState(state);
}
```
After Phase 21 — append AFTER existing window-state save:
```csharp
var snapshot = _historyService.PeekLastSnapshot();
if (snapshot != null)
{
    _historyService.SaveHistory(snapshot);   // SYNC — never SaveHistoryAsync (D-09)
}
```
Anti-pattern reminder (RESEARCH §Anti-Patterns): no `async void OnClosing`, no `.Wait()` on the async variant, no `.GetAwaiter().GetResult()`. Synchronous `SaveHistory` only.

---

### `CCInfoWindows.Tests/Services/UsageHistoryServiceTests.cs` (unit test, file I/O round-trip)

**Analog:** Self — 6 tests already follow the exact pattern Phase 21 needs.

**Imports pattern** (`UsageHistoryServiceTests.cs:1-2`):
```csharp
using CCInfoWindows.Models;
using CCInfoWindows.Services;
```

**Class declaration + temp-dir + IDisposable pattern** (`UsageHistoryServiceTests.cs:6-15`):
```csharp
public sealed class UsageHistoryServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly UsageHistoryService _sut;

    public UsageHistoryServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _sut = new UsageHistoryService(_tempDirectory);
    }
    // ... [Fact] methods ...

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
```
NEW Phase-21 tests live in this same class. Reuse the existing `_sut` and `_tempDirectory`. For tests that need TWO services (e.g. `SaveSync_VS_SaveAsync_ProducesByteIdenticalJson`), construct local additional `UsageHistoryService(localGuidDir)` instances inside the test body and clean them up via `try/finally` (mirrors RESEARCH Example 3).

**Round-trip test pattern** (`UsageHistoryServiceTests.cs:40-65`):
```csharp
[Fact]
public void SaveThenLoad_RoundTrip_PreservesAllFields()
{
    var now = DateTimeOffset.UtcNow;
    var history = new UsageHistory
    {
        ResetsAt = now.AddHours(3),
        Points =
        [
            new UsageHistoryPoint { Timestamp = now.AddMinutes(-10), Utilization = 0.25 },
            new UsageHistoryPoint { Timestamp = now.AddMinutes(-5),  Utilization = 0.50 },
            new UsageHistoryPoint { Timestamp = now,                 Utilization = 0.75 }
        ]
    };

    _sut.SaveHistory(history);
    var loaded = _sut.LoadHistory();

    Assert.Equal(history.ResetsAt, loaded.ResetsAt);
    Assert.Equal(3, loaded.Points.Count);
    // ...
}
```
NEW `SaveHistoryAsync_RoundTrip_PreservesAllFields` is the same shape with `[Fact]` → `[Fact] public async Task` and `_sut.SaveHistory(history)` → `await _sut.SaveHistoryAsync(history)`.

**Clear-then-load test pattern (basis for `Logout_ThenClose_DoesNotRecreateHistoryFile`)** (`UsageHistoryServiceTests.cs:67-82`):
```csharp
[Fact]
public void ClearHistory_DeletesFile_SubsequentLoadReturnsEmpty()
{
    var history = new UsageHistory { /* ... */ };
    _sut.SaveHistory(history);

    _sut.ClearHistory();
    var result = _sut.LoadHistory();

    Assert.Empty(result.Points);
    Assert.Null(result.ResetsAt);
}
```

**New tests required (per RESEARCH §Phase Requirements → Test Map):**
- `SaveHistoryAsync_RoundTrip_PreservesAllFields` (HIST-02/HIST-03)
- `SaveSync_VS_SaveAsync_ProducesByteIdenticalJson` (HIST-03; D-07 lock — see RESEARCH Example 3 verbatim)
- `ConcurrentSyncAndAsyncWrites_DoNotInterleave` (D-05)
- `PeekLastSnapshot_BeforeAnySave_ReturnsNull` (D-04)
- `PeekLastSnapshot_AfterSave_ReturnsLastSavedHistory` (D-04)
- `PeekLastSnapshot_AfterClear_ReturnsNull` (D-13)
- `WriteFails_DoesNotUpdateSnapshot` (RESEARCH Pitfall 2 — recommended)
- `WindowReset_ClearsPointsAndPersists` (HIST-04 verification)
- `FirstPoll_AfterAppStart_DoesNotEraseHistory` (HIST-05 verification — exercises `IsWindowReset(null, x)` returning false)

---

### `CCInfoWindows.Tests/ViewModels/MainViewModelHistoryTests.cs` (NEW, optional)

**Analog:** `Tests/ViewModels/MainViewModelAuthFlowTests.cs` (full-DI Moq factory)

**Imports pattern** (`MainViewModelAuthFlowTests.cs:1-7`):
```csharp
using CCInfoWindows.Messages;
using CCInfoWindows.Models;
using CCInfoWindows.Services.Interfaces;
using CCInfoWindows.ViewModels;
using CCInfoWindows.Views;
using Moq;
```

**Full-DI factory pattern with `Mock<IUsageHistoryService>`** (`MainViewModelAuthFlowTests.cs:18-47`):
```csharp
private static (MainViewModel vm, Mock<INavigationService> nav) CreateViewModel()
{
    var credentialService = new Mock<ICredentialService>();
    var navigationService = new Mock<INavigationService>();
    var apiService = new Mock<IClaudeApiService>();
    var settingsService = new Mock<ISettingsService>();
    settingsService.Setup(s => s.LoadSettings()).Returns(new AppSettings());
    var historyService = new Mock<IUsageHistoryService>();
    var jsonlService = new Mock<IJsonlService>();
    jsonlService.Setup(s => s.Sessions).Returns([]);
    var pricingService = new Mock<IPricingService>();
    pricingService.Setup(s => s.EnsurePricesLoadedAsync()).Returns(Task.CompletedTask);
    var updateService = new Mock<IUpdateService>();
    var bridge = new Mock<IWebViewBridge>();
    var burnRate = new Mock<IBurnRateNotificationService>();

    var vm = new MainViewModel(
        credentialService.Object,
        navigationService.Object,
        apiService.Object,
        settingsService.Object,
        historyService.Object,
        jsonlService.Object,
        pricingService.Object,
        updateService.Object,
        bridge.Object,
        burnRate.Object);

    return (vm, navigationService);
}
```
Phase 21 returns `(vm, Mock<IUsageHistoryService> historyMock)` instead, and uses `historyMock.Verify(h => h.SaveHistoryAsync(It.IsAny<UsageHistory>()), Times.AtLeastOnce)` to verify the await-chain swap (HIST-02). Mock setup for `LoadHistory()` returns a seeded `UsageHistory` to drive the `IsWindowReset` paths.

---

## Shared Patterns

### SemaphoreSlim Concurrency Guard
**Source:** `Services/LiteLLMPricingService.cs:26,43-57`
**Apply to:** `Services/UsageHistoryService.cs` — both `SaveHistory`, `SaveHistoryAsync`, and `ClearHistory`
```csharp
private readonly SemaphoreSlim _writeLock = new(1, 1);

// async caller:
await _writeLock.WaitAsync();
try { /* work — best-effort try/catch nested inside */ }
finally { _writeLock.Release(); }

// sync caller:
_writeLock.Wait();
try { /* work — best-effort try/catch nested inside */ }
finally { _writeLock.Release(); }
```

### Best-Effort I/O Try/Catch
**Source:** `Services/UsageHistoryService.cs:54-63, 68-75`
**Apply to:** All write methods — preserve existing semantics
```csharp
try
{
    // I/O work
}
catch
{
    // Best-effort — don't crash the app
}
```
MUST sit INSIDE the semaphore `try { ... } finally { _writeLock.Release(); }` so the lock always releases.

### Static `JsonSerializerOptions` Singleton
**Source:** `Services/UsageHistoryService.cs:17-20`
**Apply to:** Both `SaveHistory` and `SaveHistoryAsync` — share ONE instance for D-07 byte-identity
```csharp
private static readonly JsonSerializerOptions JsonOptions = new()
{
    WriteIndented = true
};
```
**Anti-pattern in codebase to NOT copy:** `ClaudeApiService.SaveCacheAsync:126-129` constructs per-call options. That breaks D-07 if mirrored.

### Service Locator Pattern in Window Code-Behind
**Source:** `MainWindow.xaml.cs:28-36`
**Apply to:** New `_historyService` field in `MainWindow.xaml.cs`
```csharp
private readonly IXxxService _xxxService;
// in constructor:
_xxxService = App.Services.GetRequiredService<IXxxService>();
```

### xUnit Temp-Dir + IDisposable Test Isolation
**Source:** `CCInfoWindows.Tests/Services/UsageHistoryServiceTests.cs:6-15, 120-126`
**Apply to:** All new Phase-21 tests in the same class — reuse existing `_tempDirectory`/`_sut`
```csharp
public sealed class UsageHistoryServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly UsageHistoryService _sut;

    public UsageHistoryServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _sut = new UsageHistoryService(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }
}
```

### Full-DI Moq Factory for VM Tests (optional new test file)
**Source:** `CCInfoWindows.Tests/ViewModels/MainViewModelAuthFlowTests.cs:18-47`
**Apply to:** Optional new `MainViewModelHistoryTests.cs` for HIST-02 await-chain verification
- Construct ALL 10 `Mock<I*>` services
- Required setups: `settingsService.LoadSettings → new AppSettings()`, `jsonlService.Sessions → []`, `pricingService.EnsurePricesLoadedAsync → Task.CompletedTask`
- For history-flow tests: `historyService.Setup(h => h.LoadHistory()).Returns(seededHistory)` and `historyService.Verify(h => h.SaveHistoryAsync(...), Times.X)`

---

## No Analog Found

None. Every Phase-21 file has a strong codebase analog. The phase is pure extension — no new architectural primitives.

---

## Metadata

**Analog search scope:**
- `CCInfoWindows/CCInfoWindows/Services/` (full)
- `CCInfoWindows/CCInfoWindows/Services/Interfaces/` (full)
- `CCInfoWindows/CCInfoWindows/ViewModels/` (full)
- `CCInfoWindows/CCInfoWindows/MainWindow.xaml.cs` (full)
- `CCInfoWindows.Tests/Services/` (full)
- `CCInfoWindows.Tests/ViewModels/` (full)

**Files scanned:** 30 (24 production source files via Glob `Services/**`, `ViewModels/**`, `Models/**`; 24 test files via `CCInfoWindows.Tests/**/*Tests.cs` enumeration; cross-referenced via 3 `Grep` calls for `SemaphoreSlim`, `Async\b.*\(`, `_previous`, `_historyService`/`IUsageHistoryService`)

**Pattern extraction date:** 2026-05-06
