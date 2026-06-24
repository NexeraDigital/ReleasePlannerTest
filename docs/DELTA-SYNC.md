# Delta sync: querying recently-changed project items

How a source system can pull **only the project items that changed** since its last sync — without
webhooks and without scanning every issue. Every claim here was verified by live introspection and
tests against a real org project; see [Evidence](#evidence).

## TL;DR

- Each project item has a **`updatedAt`** timestamp that moves whenever a field value changes
  (even if the underlying issue is untouched).
- You can **filter items server-side by last change**: `items(query: "updated:>=<date>")`.
- That filter is **date-granularity** (time is ignored) — so refine to an exact instant by comparing
  the returned **`item.updatedAt`** (a full timestamp) to your watermark.
- Field values also carry **`updatedAt` + `creator`**, giving you per-field change time and
  attribution (use the creator for loop suppression).

Library methods:
- `IGitHubProjectsClient.GetItemsChangedSinceAsync(projectId, DateOnly sinceDay)` — server-side day filter.
- `IGitHubProjectsClient.GetProjectItemValuesAsync(projectId)` — full scan with values + timestamps.
- Demo: `dotnet run --project src/GitHubProjectConnection.App -- --list-changed`.

## The recommended delta loop

```
watermark = <last successful sync instant, stored by the source system>

1. sinceDay = watermark.Date            // step back to a whole day (the server filter is per-day)
2. items   = GetItemsChangedSinceAsync(projectId, sinceDay)   // server-side narrow
3. changed = items.Where(i => i.UpdatedAt > watermark)        // client-side refine to the exact instant
4. for each changed item:
      if item's latest field change was made by OUR app  -> skip (loop suppression)
      else push the GitHub-owned field values back to the source system
5. watermark = max(changed.UpdatedAt, watermark)              // advance with a small overlap; dedup by item id
```

- **Step 2** keeps the fetch small even on large projects (only items touched that day or later).
- **Step 3** gives you minute/second precision even though the server filter is per-day.
- **Step 5**: advance the watermark from the max `UpdatedAt` you saw; re-query with a small overlap
  (e.g. `watermark - 1 day` at the day filter) and **dedup by item id** to tolerate equal-second
  timestamps and clock skew.

## Constraints you must design around

### 1. Server filter is date-granularity (verified)
`updated:>2026-06-24T00:00:00Z` returns **0** (treated as `> the date`), while `updated:>2026-06-23`
returns all of today's items. The time component is dropped. → Use the day filter to *narrow*, then
compare `item.UpdatedAt` client-side to get an exact-instant delta. Relative forms work too
(`updated:>=@today-1d`, `@today-7d`).

### 2. No ordering by `updatedAt` (verified)
`items(orderBy:)` only supports `POSITION` (board order). You cannot ask for "the N most-recently
changed" in order — but with the `updated:` filter you don't need to; you fetch the changed set and
sort client-side.

### 3. Item timestamp vs issue timestamp (important)
Editing a **project field value** bumps the **item's** `updatedAt` but **not the issue's**
`updatedAt` (verified). Therefore:
- `GET /repos/.../issues?since=` and Search `updated:` (issue-centric) **miss property-only changes** —
  don't use them to detect field-value edits.
- GitHub's documented definition of "updated" is issue-centric, so whether the *server* `updated:`
  filter reflects field-value edits couldn't be proven conclusively (all test items shared one day).
  **Be safe:** treat `item.UpdatedAt` (which provably moves on field edits) as the source of truth.
  If you need a guarantee, use `GetProjectItemValuesAsync` (full scan, no server filter) and compare
  `item.UpdatedAt` client-side — that cannot miss a property change. Keep the item count bounded by
  archiving completed items.

### 4. No optimistic concurrency on field values
GitHub has no ETag/version on a project field value; writes are **last-write-wins**. For two-way sync:
- Define a **per-field owner** (which side wins for each property).
- Use **timestamps** (`field.UpdatedAt`) and the watermark to order changes.
- Suppress your own writes: when you write to GitHub, the item's `updatedAt` bumps and the field's
  `creator` becomes your App — skip those on the way back (see attribution below).

## Attribution & loop suppression

Each field value exposes `updatedAt` and `creator` (the actor who last set it). `--list-changed`
prints, per item, the most recently changed field and its author, e.g.:

```
#7  item.updatedAt=2026-06-24 20:08:20Z  Organization=Microsoft 365  latestField=ServiceTreeId=… by test-project-automation
```

`test-project-automation` is the App itself. When syncing GitHub → source, **skip changes whose
author is your App** so your own writes don't echo back into the source and create a loop.

## What about issue-native changes?

For changes to the **issue itself** (title, body, state, labels, assignees, comments), the efficient
delta is the REST endpoint `GET /repos/{owner}/{repo}/issues?since=<ISO8601>&sort=updated&direction=asc`
— but remember it does **not** cover project field values. A complete two-way sync watches **both**:
project items (via `updated:` / `item.updatedAt`) for properties, and issues (`since`) for issue fields.

## Evidence

- GraphQL schema (live introspection): `ProjectV2.items(query:, orderBy:)`, `ProjectV2ItemOrderField = { POSITION }`,
  `ProjectV2Item.updatedAt`, and field-value types exposing `updatedAt` + `creator`.
- Filter works (node counts): `updated:>2030-01-01` → 0, `updated:>2026-06-01` → all, `Organization:Security` → 3.
- Date granularity: `updated:>2026-06-24T00:00:00Z` → 0 vs `updated:>2026-06-23` → all.
- Field edit moves `item.updatedAt` but not `issue.updatedAt` (before/after on issue #12).
- All against `github.com/orgs/NexeraDigital/projects/19`.

## Official sources

- [Filtering projects — `updated:` qualifier](https://docs.github.com/en/issues/planning-and-tracking-with-projects/customizing-views-in-your-project/filtering-projects)
- [REST: List repository issues — `since`/`sort`/`direction`](https://docs.github.com/en/rest/issues/issues?apiVersion=2022-11-28)
- [GraphQL reference — objects / input objects](https://docs.github.com/en/graphql/reference/objects)
