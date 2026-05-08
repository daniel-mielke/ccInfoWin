# Phase 24: Dispatcher Foundation & Marshaling Convention - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-05-08
**Phase:** 24-Dispatcher-Foundation-Marshaling-Convention
**Areas discussed:** G-1 Convention-Test Mechanism (DISPATCH-06)

---

## Gray Area Selection

| Option | Description | Selected |
|--------|-------------|----------|
| Convention-Test-Mechanik (DISPATCH-06) | Pure Reflection vs. `[ThreadSafeReceive]`/`[RequiresMarshal]` Attribute-Paar vs. hybrid. REQUIREMENTS.md sagt explizit "30-min Phase-24 spike entscheidet". Direkt planungsblockierend. | ✓ |
| `_dispatcherQueue` Lifecycle / Cold-Path-Null-Risk (PITFALLS C2-P2) | Constructor-set vs. lazy-resolve vs. DI-injected. | |
| C-1 fire-and-forget surfacing pattern (PITFALLS C1-P1) | Discard `_ =` vs. continuation vs. expliziter await. | |
| Scope-Decisions: NuGet bumps + C2-P3 double-registration | Bundle vs. separate Plan; Phase 24 vs. Phase 28. | |

**User's choice:** Only G-1 Convention-Test mechanism. Other areas delegated to Plan Phase with PITFALLS.md anchors as default playbook.
**Notes:** User judgment: PITFALLS.md and research/SUMMARY.md already provide explicit recommendations for areas 2–4; only DISPATCH-06 has a documented "spike decides" open question requiring user input on architectural direction.

---

## G-1 Convention-Test Mechanism

### Q1: Test enforcement mechanism

| Option | Description | Selected |
|--------|-------------|----------|
| Pure Reflection — Method-body inspection via Mono.Cecil or Roslyn | Test loads assembly, parses MSIL/AST, asserts first statement is TryEnqueue. Fragile against compiler inlining, source generators, helper-method wrappers. Mono.Cecil = new NuGet (violates "no new packages"). | |
| `[ThreadSafeReceive]` / `[RequiresMarshal]` Attribute-Paar | Required attribute marker; test checks attribute presence via reflection. Robust, simple ~30 LOC. Existing `IRecipient<>` need migration (1 attribute per method). | ✓ |
| Hybrid: Attribute + Source-Inspection (Reflection over body string) | Attribute marks intent; test inspects MethodBody.GetILAsByteArray() for TryEnqueue calls. Maximum safety but higher complexity. | |
| Manual Code-Review Checklist (no automated test) | Documentation only; PR reviewer checks manually. **Violates REQ DISPATCH-06** which mandates `MessengerThreadingConventionTests` xUnit class. | |

**User's choice:** Attribute-Paar (Recommended).
**Notes:** None.

### Q2: `[ThreadSafeReceive]` shape — required reason argument?

| Option | Description | Selected |
|--------|-------------|----------|
| Pflicht-String 'Reason' im Konstruktor | `[ThreadSafeReceive("specific reason")]` — convention test asserts non-empty Reason. Forces articulation of WHY at write time. ~5 LOC more in attribute. | ✓ |
| Optional (kein Pflicht-Argument) | `[ThreadSafeReceive]` reicht; Reason als Inline-Kommentar empfohlen aber nicht erzwungen. Code-Reviewer muss Inline-Comment selbst prüfen. | |
| Optional + Convention-Test prüft Inline-Comment-Präsenz | Attribut leer; Test inspiziert .cs-Source-File auf Kommentar oberhalb der Methode. Mehr Magie, mehr potenzielle False Positives bei Refactor. | |

**User's choice:** Pflicht-String 'Reason' im Konstruktor (Recommended).
**Notes:** None.

### Q3: `[RequiresMarshal]` — explicit marker or implicit default?

| Option | Description | Selected |
|--------|-------------|----------|
| Implizit (kein Marker nötig) — Default ist 'must marshal' | Convention test asserts: jede `IRecipient<>.Receive` ist entweder `[ThreadSafeReceive(reason)]`-attributiert ODER der Method-Body enthält textuell `_dispatcherQueue.TryEnqueue(`. Kein zusätzliches Attribut nötig im Default-Fall — ergonomisch, weniger Boilerplate. | ✓ |
| Explizit — `[RequiresMarshal]` muss gesetzt werden | Symmetrisches Paar. Klarer in der Lesbarkeit, aber doppelter Boilerplate. Skaliert schlecht über die ~5+ Phase 25-27 neuen Receive-Handler. | |
| Explizit + zusätzliche Compile-Time-Prüfung via Source-Generator | `[RequiresMarshal]` Pflicht; CommunityToolkit-Style Source-Generator emittiert Compiler-Warning bei Verletzung. Maximale Sicherheit, aber neue Komplexitätsklasse — widerspricht "no new packages" und Phase-24-Time-Box. | |

**User's choice:** Implizit — Default ist 'must marshal' (Recommended).
**Notes:** None.

### Q4: Test failure surface — xUnit or MSBuild?

| Option | Description | Selected |
|--------|-------------|----------|
| xUnit-Test-Failure im normalen Test-Run | `MessengerThreadingConventionTests` läuft als regulärer xUnit-Test in der Test-Suite. CI bricht den Build, lokaler Dev sieht roten Test in VS Test Explorer. Konsistent mit `ResourceCoverageTests` (v1.4 L10N-Convention-Test) und `IDispatcherTimer`-Lifecycle-Tests. | ✓ |
| MSBuild-Custom-Target — Build-Fehler | Test als Pre-Build-Hook in `.csproj`. Frühere Erkennung, aber: erfordert MSBuild-XML-Änderung, schlägt fehl beim ersten Refactor wo das Hilfs-Assembly nicht gefunden wird. Überkomplex für eine Convention. | |
| Beide — xUnit-Test + zusätzlicher MSBuild-Hook | Belt-and-suspenders. Doppelte Wartung. Nicht empfohlen für v1.5. | |

**User's choice:** xUnit-Test-Failure im normalen Test-Run (Recommended).
**Notes:** None.

### Continuation Check

| Option | Description | Selected |
|--------|-------------|----------|
| Fertig — schreib CONTEXT.md | Decisions 1-4 reichen. Areas 2-5 fallen in Plan-Phase mit PITFALLS-Anchors. | ✓ |
| Eine weitere G-1-Frage — Migrations-Scope existierender `IRecipient<>` | Welche bestehenden `IRecipient<>`-Implementierungen werden in Phase 24 migriert? | |
| Doch noch Area 2 (`_dispatcherQueue` Lifecycle) ausdiskutieren | Constructor-set / Lazy-resolve / DI-injected. | |

**User's choice:** Fertig — schreib CONTEXT.md.
**Notes:** Migration scope of existing `IRecipient<>` was inventoried automatically by Claude during context-write (4 sites found, captured in CONTEXT.md CD-05). Areas 2–4 deferred to Plan Phase using PITFALLS.md as default playbook.

---

## Claude's Discretion

User explicitly delegated these areas to Plan Phase with PITFALLS.md as default playbook:

- **CD-01:** `_dispatcherQueue` lifecycle / cold-path null risk — anchor PITFALLS C2-P2; recommended lazy-resolve helper or DI-injection.
- **CD-02:** C-1 surfacing pattern — anchor PITFALLS C1-P1; recommended explicit discard `_ = RefreshCommand.ExecuteAsync(null);`.
- **CD-03:** NuGet patch bumps placement — anchor ROADMAP SC#5; recommended bundling with C-1/C-2 plan.
- **CD-04:** C2-P3 double-registration — anchor PITFALLS C2-P3; recommended `UnregisterAll(this)` in Phase 24.
- **CD-05:** Existing `IRecipient<>` migration scope — automatically inventoried (4 sites: 2 ViewModel, 2 MainWindow). Plan Phase decides MainWindow scope inclusion.

## Deferred Ideas

- Roslyn analyzer for G-1 — v1.6+ (per REQUIREMENTS.md Out of Scope).
- WinAppSDK 2.0 bump — v1.6+.
- Source-Generator-based G-1 check — rejected (new complexity class).
- G-2 / G-3 — Phase 26 / Phase 28.
- Pricing fire-and-forget surfacing — Phase 27 PRICING-01.
