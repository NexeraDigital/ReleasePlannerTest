# Use cases & examples

What this project can do, with runnable examples. Each item is tagged by **how** it's available:

| Tag | Meaning |
|-----|---------|
| 🟢 **Library** | A public API in the `GitHubProjects` library — inject an interface and call it. |
| 🔵 **Command** | A ready-to-run sample command: `dotnet run --project src/GitHubProjectConnection.App -- <flag>`. |
| 🟡 **Not in the library yet** | Demonstrated as a one-off script; would need a new library method to be first-class. |

Library calls assume you've injected the clients (`IGitHubIssueClient`, `IGitHubProjectsClient`,
`IGitHubFieldManager`) after `services.AddGitHubProjects(...)`. All async methods take a
`CancellationToken` (shown as `ct`).

---

## Authentication & setup

### Authenticate as a GitHub App — 🟢 Library (automatic)
JWT → installation token, cached until just before expiry. Handled internally once you register the
library; you never call it directly.
```csharp
services.AddGitHubProjects(config.GetSection("GitHubApp"));   // from config
// or
services.AddGitHubProjects(o => { o.ClientIdOrAppId = "Iv23li…"; o.PrivateKeyPem = pem; o.Owner = "my-org"; });
```

### Auto-discover the installation id — 🟢 Library (automatic)
When `GitHubApp:InstallationId` is null, it's discovered from `Owner`/`OwnerType`. On failure the
error lists every account the App is actually installed on.

### Supply the private key from a file or a secret — 🟢 Library
`GitHubAppOptions.PrivateKeyPath` (file) or `PrivateKeyPem` (inline, e.g. `GitHubApp__PrivateKeyPem`
from a secret store).

---

## Issues

### Create a new issue — 🟢 Library
```csharp
CreatedIssue issue = await issues.CreateIssueAsync(owner, repo, "Title", "Body", ct);
// issue.NodeId, issue.Number, issue.HtmlUrl
```

---

## Project items & field values

### Resolve a project by number — 🟢 Library
```csharp
string projectId = await projects.GetProjectIdAsync(owner, isOrganization: true, projectNumber: 19, ct);
```

### Read a project's custom fields — 🟢 Library
```csharp
IReadOnlyDictionary<string, ProjectField> fields = await projects.GetProjectFieldsAsync(projectId, ct);
// field.DataType, field.Options (option name -> id) for single-selects
```

### List items already in a project — 🟢 Library
```csharp
IReadOnlyList<ProjectItem> items = await projects.GetProjectItemsAsync(projectId, ct);
// item.Id, item.Number, item.Title  (NOTE: field values are NOT included — see "Audit item values")
```

### Add an existing issue/PR to a project — 🟢 Library
```csharp
string itemId = await projects.AddItemToProjectAsync(projectId, issue.NodeId, ct);
```

### Set a field value on an item — 🟢 Library
Shape is chosen automatically by the field's data type (text / number / date / single-select).
```csharp
await projects.UpdateFieldValueAsync(projectId, itemId, fields["Status"], JsonValue("In Progress"), ct);
```

### Create an issue, add it, and populate all its fields — 🔵 Command (default)
```bash
dotnet run --project src/GitHubProjectConnection.App
```

### Re-populate every existing item after a schema change — 🔵 Command
```bash
dotnet run --project src/GitHubProjectConnection.App -- --populate-existing
```

---

## Custom field (custom property) definitions

### Create a custom field — 🟢 Library
```csharp
await fields.CreateFieldAsync(projectId, "SINGLE_SELECT", "Priority",
    new[] { new SingleSelectOption("High", "RED"), new SingleSelectOption("Low", "GRAY") }, ct);
// dataType also: "TEXT", "NUMBER", "DATE" (no options for those)
```

### Rename a field / replace its options — 🟢 Library
```csharp
await fields.UpdateFieldAsync(fieldId, name: "Release Confidence", options: fullOptionList, ct);
```

### Delete a custom field — 🟢 Library
```csharp
await fields.DeleteFieldAsync(fieldId, ct);   // removes the field and its values across items
```

### Self-cleaning create→update→delete demo — 🔵 Command
```bash
dotnet run --project src/GitHubProjectConnection.App -- --manage-fields-demo
```

### Convert a TEXT field to a dropdown — 🔵 Command (destructive)
Delete + recreate as single-select; clears that field's existing values.
```bash
dotnet run --project src/GitHubProjectConnection.App -- --convert-dropdowns
```

---

## Dropdown option management (non-destructive)

### Add options to a dropdown WITHOUT clearing existing ones — 🟢 Library
Fetch → merge (dedup by name, case-insensitive) → write back preserving ids. Idempotent.
```csharp
await fields.AddSingleSelectOptionsAsync(projectId, "Organization",
    new[] { new SingleSelectOption("Contoso", "PINK") }, ct);
```

### Read a single-select field's full option details — 🟢 Library
Returns each option's id, name, color, description — for safe round-tripping.
```csharp
SingleSelectFieldDetail? field = await projects.GetSingleSelectFieldAsync(projectId, "Organization", ct);
```

### Lower-level option edit (keep/reorder/recolor) — 🟢 Library
Send the FULL option list; include each existing option's `Id` to preserve it (and its item values).
```csharp
await fields.UpdateFieldAsync(field.FieldId, name: null, options: mergedListWithIds, ct);
```

### Add a dropdown option (idempotent) then revert — 🔵 Command
```bash
dotnet run --project src/GitHubProjectConnection.App -- --add-option-demo
```

### Validate non-destructive modification (add → verify → revert) — 🔵 Command
```bash
dotnet run --project src/GitHubProjectConnection.App -- --validate-dropdown
```

---

## Verification / diagnostics

### Read per-item field VALUES (with timestamps + author) — 🟢 Library
```csharp
IReadOnlyList<ProjectItemDetail> items = await projects.GetProjectItemValuesAsync(projectId, ct);
// item.UpdatedAt, item.ValueOf("Organization"), and per-field UpdatedAt + UpdatedBy
```

### Delta sync: query only items changed since a watermark — 🟢 Library / 🔵 Command
Server-side `updated:` filter (date-granularity); refine to an exact instant with `item.UpdatedAt`.
See [DELTA-SYNC.md](DELTA-SYNC.md) for the full loop and caveats.
```csharp
var changed = await projects.GetItemsChangedSinceAsync(projectId, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1), ct);
```
```bash
dotnet run --project src/GitHubProjectConnection.App -- --list-changed
```

### List all commands — 🔵 Command
```bash
dotnet run --project src/GitHubProjectConnection.App -- --help
```

---

## Cross-cutting (applies to every call)

- **HTTP resilience** — 🟢 Library: retry / circuit-breaker / timeout that honors GitHub's
  `Retry-After` and `x-ratelimit-reset` (configured by `AddGitHubProjects`).
- **Cancellation & error mapping** — 🔵 Command: Ctrl+C flows through every call; `GitHubApiException`
  → exit code 1, cancellation → 130 (in the sample host).
- **Consume from your own app** — 🟢 Library: `AddGitHubProjects(...)` + inject the three interfaces,
  instead of running the sample console app.

---

## Summary

| # | Use case | Availability |
|---|----------|--------------|
| 1 | Authenticate as a GitHub App (token caching) | 🟢 Library (automatic) |
| 2 | Auto-discover installation id | 🟢 Library (automatic) |
| 3 | Private key from file or secret | 🟢 Library |
| 4 | Create an issue | 🟢 Library |
| 5 | Resolve a project by number | 🟢 Library |
| 6 | Read custom fields | 🟢 Library |
| 7 | List project items | 🟢 Library |
| 8 | Add an issue/PR to a project | 🟢 Library |
| 9 | Set a field value (text/number/date/single-select) | 🟢 Library |
| 10 | Create issue + add + populate fields | 🔵 Command (default) |
| 11 | Re-populate every existing item | 🔵 Command (`--populate-existing`) |
| 12 | Create a custom field | 🟢 Library |
| 13 | Rename a field / replace options | 🟢 Library |
| 14 | Delete a custom field | 🟢 Library |
| 15 | Create→update→delete field demo | 🔵 Command (`--manage-fields-demo`) |
| 16 | Convert TEXT field to dropdown (destructive) | 🔵 Command (`--convert-dropdowns`) |
| 17 | Add dropdown options without clearing existing | 🟢 Library (`AddSingleSelectOptionsAsync`) |
| 18 | Read single-select option details (id/color/desc) | 🟢 Library |
| 19 | Lower-level option edit preserving ids | 🟢 Library (`UpdateFieldAsync`) |
| 20 | Add option (idempotent) then revert | 🔵 Command (`--add-option-demo`) |
| 21 | Validate non-destructive modification | 🔵 Command (`--validate-dropdown`) |
| 22 | Read per-item field values (timestamps + author) | 🟢 Library (`GetProjectItemValuesAsync`) |
| 23 | Delta sync — items changed since a watermark | 🟢 Library (`GetItemsChangedSinceAsync`) / 🔵 (`--list-changed`) |
| 24 | List commands | 🔵 Command (`--help`) |
