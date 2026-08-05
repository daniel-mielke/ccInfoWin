---
name: Backlog — Org-ID picker for users with multiple Anthropic accounts
description: Users with both personal and team/org Anthropic accounts under the same email can end up with the wrong org-id cached in `CCInfoWindows/claude-org`, causing the usage endpoint to return 0% across the board. The app needs a way to (a) detect multi-org accounts, (b) let the user pick which org to track, (c) re-resolve the org-id when usage looks suspicious (e.g., all zeros for an active user).
type: project
originSessionId: 4fcfe4f9-d257-456b-bc4f-1109b37175ac
---
# Org-ID picker for multi-account users

**Reported:** 2026-05-07 by user during v1.4 UAT.

## Symptom

User has two Anthropic accounts under the same email: a personal account and a team/org account (Smart Commerce). The browser correctly shows the team account's usage (Aktuelle Sitzung 14%, Wochenlimit 26%, Sonnet 5%). The app shows 0% across all metrics.

## Root cause hypothesis

The app persists an org-id under `CCInfoWindows/claude-org` (Credential Manager). If this points to the personal account (which has zero usage), every API call to `/api/organizations/{wrong-org-id}/usage` returns valid JSON with all zeros — no error path triggers, the data just looks empty. There's `TryMigrateOrgIdAsync` in `ClaudeApiService` but it likely runs only when the org-id is missing, not when it's present-but-wrong.

## What to build

A way for the user to:

1. **Detect** when the resolved org has zero usage despite an active session (heuristic: 5+ consecutive polls all returning `utilization: 0` AND the user is signed in)
2. **List** all available orgs from the Anthropic API (`/api/organizations` likely)
3. **Pick** which org to track
4. **Persist** the selected org-id in `claude-org` (overriding the auto-resolved value)
5. **Re-resolve** trigger from a Settings UI button: "Re-detect organization"

## Why this matters

This silently breaks the app for any user with both a personal and a team Anthropic account. No error message, no clue what's wrong — just consistent 0% values that look like an app bug. Hard to diagnose without filesystem inspection.

## Verify before scoping

- Confirm the actual `claude-org` value via Credential Manager AND verify which org-id corresponds to which account by hitting the Anthropic API directly.
- Check whether `TryMigrateOrgIdAsync` already supports a "force re-resolution" mode or whether a new method is needed.
- Check if there's a `/api/organizations` listing endpoint or if we have to derive available orgs from another API.
