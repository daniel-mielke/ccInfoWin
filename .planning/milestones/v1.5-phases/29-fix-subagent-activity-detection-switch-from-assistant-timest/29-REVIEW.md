---
phase: 29
plan: 01
review_date: 2026-05-19
status: warnings
depth: standard
findings_count: { critical: 0, warning: 2, info: 4 }
---

# Phase 29 Code Review

## Summary

Der mtime-Cutoff-Patch in `JsonlService.BuildSubagentContext` ist semantisch korrekt, performance-positiv (stale Files werden nicht mehr geparst) und macOS-konform. Die `DateTimeOffset`-Konvertierung, das Catch-Verhalten, der `entries.Count == 0`-Guard und die UTC-Arithmetik wurden sauber umgesetzt. Es gibt **keine Blocker** — der Phase-29-Code darf shippen.

Aber: der **neue Test `JsonlServiceSubagentTests`** hat zwei Robustheits-Lücken, die ihn in der CI fragil machen können (WR-01: Test-Pollution-Risiko über `D--myProjects-ccInfoWin`-Hardcode; WR-02: Cleanup-Lücke beim "Stop()"-Pfad bei Assertion-Failure). Beide sind Warning, kein Blocker — die Tests laufen heute grün, und beide Risiken sind environmental.

## Findings

### Critical (0)

Keine.

### Warning (2)

#### WR-01: Test koppelt sich an einen real existierenden Pfad auf der Maintainer-Maschine

**File:** `CCInfoWindows.Tests/Services/JsonlServiceSubagentTests.cs:18`
**Issue:**
```csharp
private const string ProjectDirName = "D--myProjects-ccInfoWin";
```
Die Tests verlassen sich darauf, dass `SessionNameHelper.DecodeProjectDirectory("D--myProjects-ccInfoWin")` → `"ccInfoWin"` zurückgibt UND dass der CI/Dev-Pfad `D:\myProjects\ccInfoWin\` existiert (siehe `JsonlService.IsValidProjectDirectory` Zeile 795–806, das via `Directory.Exists(cwd)` validiert). Auf einem CI-Runner ohne `D:\myProjects\ccInfoWin\` oder einer Maintainer-Maschine mit anderem Repo-Layout (z. B. `C:\src\ccInfoWin\`) wird der Pfad in `IsValidProjectDirectory` als ungültig verworfen und die Subagents fallen aus.

**Warum es heute trotzdem grün ist:** Die Tests setzen `data.Cwd` NIEMALS auf eine cwd, weil das Fixture-JSONL kein `cwd`-Feld schreibt (siehe `WriteAssistantJsonlLine` Zeilen 207–234). Dadurch greift die DROPDOWN-03-Softening, und `RebuildSessionsList` resolved den Pfad NICHT über `IsValidProjectDirectory`. Aber die `GetContextWindow(projectDirName)`-Surface in Zeile 139–170 ruft `IsValidProjectDirectory` gar nicht auf — sie greift direkt auf `_projectData`. Das ist Glück, kein Design. Wenn jemand später `GetContextWindow` um einen cwd-Existence-Check erweitert (was Phase-25 Backlog explizit nennt), bricht die Test-Suite still.

**Fix:** Verwende einen synthetischen Projektnamen, dessen Decode niemals auf einen real existierenden Pfad zeigt — z. B. `"X--phase29-subagent-fixture"`. Der Display-Name resolved trotzdem korrekt via `DecodeProjectDirectory`, und die Tests werden hermetisch:
```csharp
// Synthetic project name — no dependency on Maintainer machine layout.
private const string ProjectDirName = "X--phase29-subagent-fixture";
```

#### WR-02: `svc.Stop()` läuft NICHT, wenn eine Assertion vorher wirft — `IDisposable` reicht nicht

**File:** `CCInfoWindows.Tests/Services/JsonlServiceSubagentTests.cs:70, 102, 143`
**Issue:** Jeder Test endet mit `svc.Stop()`, der `DisposeWatcher()` + `DisposeDebounceTimer()` aufruft. Bei einem Assertion-Failure (z. B. `Assert.Contains` schlägt fehl) wirft xUnit eine Exception VOR der `svc.Stop()`-Zeile. Folge: der `FileSystemWatcher` bleibt aktiv, hält ein Handle auf `_tempDir`, und der `Dispose()`-Cleanup-Pfad (`Directory.Delete(_tempDir, recursive: true)`) wirft `IOException` — was in Zeile 33 dann zwar geschluckt wird ("AV / handle race"), aber den Temp-Verzeichnis-Müll auf der Maschine zurücklässt. Über viele Test-Runs hinweg leakt das.

**Warum es heute trotzdem grün ist:** Alle 3 Tests passen, `svc.Stop()` läuft also. Das Risiko triggert ausschließlich bei Test-Failure.

**Fix:** `using` deklarieren statt manuellem `Stop()`, oder `svc.Stop()` in den `Dispose()`-Pfad ziehen. `JsonlService` implementiert `IDisposable` (Zeile 256: `public void Dispose() => Stop();`):
```csharp
using var svc = new JsonlService(projectsDirectoryOverride: _tempDir);
await svc.InitializeAsync();
// ... Act ...
// kein expliziter svc.Stop() mehr — using cleanup ist Exception-safe
```
Spart außerdem die 3 explizit duplizierten `svc.Stop()`-Aufrufe (DRY pro CLAUDE.md Clean-Code-Regel).

### Info (4)

#### IN-01: Test-Methoden sind `async Task`, aber `await` nur den DI-Init — kein echter Async-Vorteil

**File:** `CCInfoWindows.Tests/Services/JsonlServiceSubagentTests.cs:48, 82, 115`
**Issue:** Alle drei Tests sind `async Task`, aber das einzige `await` ist `svc.InitializeAsync()`. Das entspricht der Phase-25-Convention (`JsonlServiceColdStartTests`) und ist okay — nur erwähnt, weil ein synchroner Test (mit `.GetAwaiter().GetResult()`) den xUnit-Async-State-Machine-Overhead spart. Nicht actionable; aber wenn der `JsonlService` jemals eine synchrone Init-Variante bekommt, kann das vereinfacht werden.
**Fix:** Keine Änderung empfohlen — Konsistenz mit `JsonlServiceColdStartTests` ist wichtiger als die Mikro-Optimierung.

#### IN-02: Magic-Number `TimeSpan.FromMinutes(-5)` als Stale-Schwellwert wird 3x dupliziert

**File:** `CCInfoWindows.Tests/Services/JsonlServiceSubagentTests.cs:52, 86, 118`
**Issue:** `DateTimeOffset.UtcNow.AddMinutes(-5)` taucht 3x als "well outside 30s cutoff" auf, ohne benannte Konstante. CLAUDE.md Clean-Code-Regel: "No magic numbers". Test-Code ist von der Regel nicht ausgenommen.
**Fix:** Extrahiere als Klassen-Konstante:
```csharp
// Far outside the 30s SubagentActivityWindowSeconds — picked at 5 min to also dodge
// FAT32 / network-share 2s mtime resolution (RESEARCH.md Pitfall 6).
private static readonly TimeSpan StaleOffset = TimeSpan.FromMinutes(-5);
```

#### IN-03: `Subagent file isSidechain` Kommentar ist im Production-Code redundant

**File:** `CCInfoWindows/CCInfoWindows/Services/JsonlService.cs:715-716`
**Issue:** Der Kommentar
```csharp
// Subagent files have isSidechain=true on all entries by design —
// do not apply the sidechain filter here.
```
beschreibt die NICHT-Anwendung eines Filters, der hier weder vorhanden noch entfernt wurde. Der Code direkt darunter filtert ausschließlich auf `e.Type == "assistant"`. CLAUDE.md Clean-Code-Regel: "Minimal comments — only comment unusual behavior". Hier wird "Abwesenheit eines Filters" kommentiert — Noise.
**Fix:** Kommentar entfernen. Falls die Information wichtig ist, gehört sie an `IsRelevantAssistantEntry` (Zeile 650–652) als XML-Doc oder in einen Test-Namen.

#### IN-04: `AssertMtimeWasSet` ist defensiv gegen ein Risiko, das im Test selbst nicht reproduziert wird

**File:** `CCInfoWindows.Tests/Services/JsonlServiceSubagentTests.cs:192-199`
**Issue:** Der Helper re-readt mtime nach `SetLastWriteTimeUtc`, um RESEARCH.md Pitfall 5 (AV-Bumps) zu mitigieren. Das ist gut. Aber: die Toleranz von `1 second` ist großzügiger als nötig (NTFS hat 100 ns Auflösung), und die Fehlermeldung suggeriert "test environment hostile", obwohl es auch ein echtes Bug in `SetLastWriteTimeUtc` sein könnte. Plus: bei FAT32 / network-share mit 2s-Granularität (RESEARCH.md Pitfall 6) würde die Toleranz selbst dort grade noch greifen — Zufall, nicht Design.
**Fix:** Tighten die Toleranz oder verdoppele sie auf 3 Sekunden, um sowohl NTFS-Präzision als auch FAT32-Granularität robust abzudecken. Aktuelles `1s` ist die ungünstigste Wahl (zu eng für FAT32, zu weit für NTFS).

## Verified

Folgende Punkte aus dem `<review_focus>`-Katalog wurden geprüft und sind **explizit OK**:

1. **mtime-Konvertierungs-Idiom (`new DateTimeOffset(mtimeUtc, TimeSpan.Zero)`)** — korrekt. `File.GetLastWriteTimeUtc` garantiert `Kind=Utc`; der explizite Zero-Offset folgt RESEARCH.md Finding 3 und ist defensiver als der Single-Arg-Ctor. `JsonlService.cs:707-708`.

2. **Deletion-Race File.GetLastWriteTimeUtc** — korrekt geregelt. Per .NET-Dokumentation (RESEARCH.md Finding 2) gibt `GetLastWriteTimeUtc` auf nicht-existenten Dateien `1601-01-01Z` zurück, OHNE zu werfen. Das fällt sauber durch den `< cutoff`-Filter (Zeile 711). `ReadTailLines` wirft `IOException` bei deletion-mid-call, das wird vom existierenden Catch in Zeile 742 abgefangen.

3. **`entries.Count == 0`-Guard preserved** — bestätigt. Zeile 724 unverändert. Die Reihenfolge (mtime-Cutoff zuerst, Parse zweite, Empty-Guard dritte) ist exakt wie in RESEARCH.md Finding 5 + Pitfall 3 spezifiziert. Ein fresh-mtime-File ohne assistant-Entry surfacet nicht als leere UI-Zeile.

4. **Performance-Reihenfolge: mtime BEFORE `ReadTailLines`** — verified. `JsonlService.cs:707` (`mtimeUtc = File.GetLastWriteTimeUtc(file)`) steht VOR Zeile 714 (`var lines = ReadTailLines(file)`). Stale Files werden nicht mehr geöffnet.

5. **Old line is removed, not commented out** — verified. `grep` auf `lastEntry.Timestamp ?? DateTimeOffset.MinValue` in `JsonlService.cs` ergibt 0 Treffer. CLAUDE.md Clean-Code-Regel "Delete commented-out code" erfüllt.

6. **`SubagentActivityWindowSeconds = 30` unverändert** — verified, Zeile 30. Einziger Magic-Number-Free-Pass im Production-Code.

7. **Test-Isolation via unique temp-dir** — verified. `Path.Combine(Path.GetTempPath(), "subagent-tests-" + Guid.NewGuid().ToString("N"))` (Zeile 24) erlaubt xUnit-Parallel-Execution; keine Shared-State-Kollision.

8. **xUnit-Naming-Convention `Method_Scenario_Expectation`** — verified. Alle 3 Tests folgen dem Schema, kein `Should_`-Präfix, keine FluentAssertions (CLAUDE.md-konform).

9. **`File.AppendAllText` statt `FileStream`** — verified (Zeile 233). Schließt das Handle vor Return, sodass `SetLastWriteTimeUtc` nicht auf einer offenen Datei läuft (RESEARCH.md Pitfall 4).

10. **Test-Speed** — verified. Kein `Thread.Sleep`, kein Network, kein großer Fixture. Per Summary: 137 ms für alle 3 Tests.

11. **Catch-Strategie unverändert** — verified. `catch (IOException ex)` + `catch (UnauthorizedAccessException ex)` (`JsonlService.cs:742-749`) wie vor dem Patch. Beide Catches decken auch die `File.GetLastWriteTimeUtc`-Failure-Modes ab.

12. **Secure Coding — kein Token-/Path-Leak in Logs** — verified. Die zwei `Debug.WriteLine`-Zeilen loggen Pfade (existierten schon vor Phase 29). `Debug.WriteLine` schreibt NIEMALS auf Disk im Release-Build (gated auf `DEBUG`-Symbol via `[Conditional("DEBUG")]`), also keine Verletzung der CLAUDE.md "No sensitive data in logs"-Regel.

13. **No new I/O or attack surface** — verified. `GetFileAttributesEx` (intern für `File.GetLastWriteTimeUtc`) ist metadata-only, kein File-Handle, kein Privilege-Escalation-Risk (RESEARCH.md Finding 6).

14. **F.I.R.S.T. — Fast, Independent, Repeatable, Self-Validating, Timely** — alle 5 Kriterien erfüllt. Fast: 137ms total. Independent: Per-Test temp-dir. Repeatable: Mtime explicit via `SetLastWriteTimeUtc`. Self-Validating: `Assert.*`. Timely: in Wave 1 (RED-first via Task 1).

---

_Reviewed: 2026-05-19T07:58:00Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
