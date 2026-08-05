# Backlog

Pending work not yet assigned to a milestone.
GSD: promote via `/gsd-review-backlog`. Plan mode / ultracode: read this first.

## Open

### Carried out of v1.6 (deliberately not done there)

- **`MainViewModel` split** — 1235 lines, 48 observable properties. 5h / Weekly / Sonnet /
  Context / Statistics / Banners are clean groups. Kept out of v1.6 because a refactor that size
  next to seven feature ports makes regressions unfindable.
- **Tier-trigger semantics for long-context pricing** (`JsonlService`, `cumulativeBefore`) —
  the running input sum is per model *over the aggregation period*, not the size of the
  individual request, so a week that crosses 200k input switches every later entry to
  long-context prices regardless of request size. Probably identical upstream. Documented in
  v1.6 so task 5-4 would not silently cement it; still open.
- **Chart height 160** — derived, but ultimately taste. Revisit after the first visual UAT;
  it is one number in `MainView.xaml` plus two in `ExportHelper`.
- **v1.6 visual UAT** — could not run, workstation stayed locked (DWM returns black frames,
  UIA Invoke does not fire commands). Checklist in `.planning/STATE.md`.

## Notes

Root-cause research for shipped items: `.planning/research/rootcause-*.md`.
Changelog: `.planning/MILESTONES.md`.

