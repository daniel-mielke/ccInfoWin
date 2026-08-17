# Mock data

Fixtures for inspecting UI states that are otherwise expensive or impossible to reach on demand.
Tracked in git deliberately — the point is that the next person does not have to rebuild them.

## workflow-run — a fake workflow run

Puts a workflow row with a full tooltip on screen without waiting for a real multi-agent run.

```powershell
.\.planning\mockdata\mock-workflow.ps1            # install into the live session
.\.planning\mockdata\mock-workflow.ps1 -Remove    # take it back out (stop the app first)
```

The row appears within one poll interval and stays for 20 minutes. Re-run the script to extend it.

### What it produces

| | |
|---|---|
| Row | `⚙ wf_mock0001-000 · 15/15 Agents fertig · 358K Tokens` |
| Tooltip name | `code-clone-review` |
| Phases | 5, one of them detail-less |

### Where the numbers come from

Four facts live in four different places, which is the whole reason this fixture exists — a change
that breaks one of them leaves the others looking correct.

| Shown | Read from | Note |
|---|---|---|
| `15/15 Agents` | `subagents/workflows/<runId>/journal.jsonl` | counted from `started` / `result` lines, NOT from how many transcripts exist. Three transcripts back a 15/15 row. |
| `358K Tokens` | the three `agent-*.jsonl` | sum of `input + cache_read + cache_creation` of each agent's last assistant entry. Output tokens are consumption, not context, and are not counted. |
| Name, description, phases | `workflows/scripts/*-<runId>.js` | the `export const meta` block |
| Start time | the run directory's creation time | see below |

### No completed-run JSON

A finished run writes `workflows/<runId>.json` with the same name and summary plus an exact
`startTime`. **The app never reads it, and this fixture does not contain one.** That file appears
only at completion, and a completed run stops writing — the 30-second staleness gate has already
removed its row by the time the file exists, so any code reading it would never run against a row a
user can see. The start time comes from the run directory instead, which exists from the first agent
on. Measured against real runs, that is 2–4 s after the true start.

If you ever see workflow metadata appear only *after* a run ends, that reasoning has been undone.

### The trap built into the fixture on purpose

The script contains a JSON schema below the meta block whose properties are also called `title` and
`description`, including one phase entry reading `NOT A PHASE`. If that string appears in the
tooltip, the parser stopped bounding its search at the meta block's closing brace — every real
workflow script has such schemas below its meta.

The meta block also has a brace inside the description, one detail long enough to wrap in the
tooltip, one title long enough to wrap inside the fixed title column, and one phase with no detail.
Edit those to re-test the layout — that is what the fixture is for.

### Why the timestamps are stamped into the future

`JsonlService` only shows a run whose newest agent transcript is younger than 30 seconds. Copied
files would age out after one poll tick, so the script sets their `LastWriteTime` 20 minutes ahead.
The same trick works on a real finished run: copy its `subagents/workflows/wf_*/` into the active
session and stamp the transcripts forward.
