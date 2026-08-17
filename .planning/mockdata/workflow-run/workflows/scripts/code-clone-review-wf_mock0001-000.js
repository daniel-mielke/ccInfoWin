// MOCK workflow script for CCInfoWindows. Only the `export const meta` block below is ever read by
// the app (Helpers/WorkflowScriptMeta.cs) — the rest exists so the file looks like a real script and
// so the parser has to prove it stops at the block's closing brace.
//
// The meta block is deliberately harder than a real one:
//   - phase 2's detail is long enough to wrap in the 360-DIP tooltip
//   - phase 4's title is long enough to wrap inside the fixed title column
//   - phase 5 has no detail at all
//   - the description contains a brace, which naive brace-counting would treat as the block's end
// Change these to re-test the layout; that is what this fixture is for.

export const meta = {
  name: 'code-clone-review',
  description: 'Detect Type 1/2/3 code clones across the whole repo {mock}, verify classifications, write review md',
  phases: [
    { title: 'Detect', detail: '9 detectors partitioned by file ownership' },
    { title: 'Verify', detail: 'adversarial re-read of every reported clone pair, five batches running against the surviving findings' },
    { title: 'Report', detail: 'write .planning/reviews markdown' },
    { title: 'Consolidate findings', detail: 'merge duplicate reports' },
    { title: 'Done' },
  ],
}

// --- Everything below the block is a trap for the parser, not data. ------------------------------
// These schema properties are also called `title` and `description`. A search that is not bounded by
// the meta block's braces reports them as workflow phases.

const FINDINGS_SCHEMA = {
  type: 'object',
  properties: {
    title: { type: 'string', description: 'one line, names the duplicated logic' },
    severity: { type: 'string', enum: ['high', 'medium', 'low', 'nit'] },
    phases: [{ title: 'NOT A PHASE', detail: 'if this shows up in the tooltip, the parser is broken' }],
  },
}

phase('Detect')
const found = await parallel([])
return { found: found.length, schema: FINDINGS_SCHEMA }
