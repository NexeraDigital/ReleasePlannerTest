# GitHub Project Connection (C#)

A minimal, dependency-light C# example that authenticates as a **GitHub App**, then:

1. Creates a new **issue** (REST).
2. Adds it to a **Project (Projects V2)** and **populates custom fields** (GraphQL).

Projects V2 is **GraphQL-only** — there is no REST API for project fields, which is why
the project parts of this sample use GraphQL while issue creation uses REST.

## How it works

| Step | API | Call |
|------|-----|------|
| 1. App JWT | — | RS256 JWT signed with the App private key (`iss` = client/app id, ≤10 min) |
| 2. Installation token | REST | `POST /app/installations/{id}/access_tokens` (valid ~1 hour) |
| 3. Create issue | REST | `POST /repos/{owner}/{repo}/issues` → returns `node_id` |
| 4. Find project | GraphQL | `organization\|user(login).projectV2(number) { id }` |
| 5. Read fields | GraphQL | `ProjectV2 { fields { … options{id name} } }` |
| 6. Add item | GraphQL | `addProjectV2ItemById(projectId, contentId)` |
| 7. Set fields | GraphQL | `updateProjectV2ItemFieldValue(...)` per field |

Source files: `GitHubAppAuthenticator.cs` (auth), `GitHubClient.cs` (REST + GraphQL),
`Program.cs` (orchestration), `SampleDataGenerator.cs` (sample data), `appsettings.json` (config).

## Sample data

The values this tool writes are **sample/test data only** — they do not represent real
products, dates, people, or release plans. They exist solely to demonstrate populating a
GitHub Project's custom fields.

- `SampleDataGenerator.cs` generates randomized issue titles, bodies, and field values on
  every run, so repeated runs create distinct sample issues.
- Issues created by this tool are safe to delete. Run it only against a **test repo and
  test project**, never a production board.

## Prerequisites

> **New to GitHub Apps?** See [docs/CREATE-TEST-GITHUB-APP.md](docs/CREATE-TEST-GITHUB-APP.md)
> for a full step-by-step guide to registering a test App, installing it, and filling in
> `appsettings.json` (with a troubleshooting table). The summary below is the short version.

### 1. Create / configure a GitHub App
In the App settings, grant these **permissions**:

- **Repository → Issues: Read and write** (to create the issue)
- **Organization → Projects: Read and write** (for an org-owned project), **or**
  **Repository → Projects: Read and write** if the project is repo/user scoped

Then **install the App** on the org (or account) that owns both the repo and the project,
and **download the private key** (`.pem`).

> Classic project boards are different and deprecated — this targets the new **Projects (V2)**.

### 2. Fill in `appsettings.json`
- `GitHubApp.ClientIdOrAppId` — the App's **Client ID** (recommended) or numeric App ID.
- `GitHubApp.PrivateKeyPath` — path to the downloaded `.pem` (kept out of git via `.gitignore`).
- `GitHubApp.InstallationId` — optional; auto-discovered from `Target.Owner` if left null.
- `Target.Owner` / `OwnerType` / `Repo` / `ProjectNumber` — from the project URL
  `github.com/orgs/<owner>/projects/<ProjectNumber>`.

The issue and custom-field values are generated per run by `SampleDataGenerator.cs`
(not configured in `appsettings.json`).

Any value can also be supplied via environment variables, e.g.
`GitHubApp__PrivateKeyPath=/secure/key.pem`.

## Run

```bash
dotnet run
```

Each run creates a new **sample** issue (see [Sample data](#sample-data)). Expected output:

```
Using installation id 12345678.
Obtained installation access token.
Created issue #42: https://github.com/my-org/my-repo/issues/42
Resolved project PVT_xxx with 6 fields.
Added issue to project as item PVTI_xxx.
  ✓ Set 'Status' = "In Progress"
  ✓ Set 'Priority' = "High"
  ✓ Set 'Estimate' = 3
  ✓ Set 'Due Date' = "2026-07-01"
  ✓ Set 'Notes' = "Populated via GraphQL by the GitHub App"
Done.
```

## Notes & troubleshooting

- **`Resource not accessible by integration`** → the App is missing the Projects (or Issues)
  permission, or isn't installed on the owner. Re-grant permissions and accept them.
- **Project not found** → wrong `ProjectNumber`/`OwnerType`, or the project belongs to a
  different owner than the repo. Org projects use `OwnerType: organization`; personal
  projects use `user`.
- **Single-select option not found** → the value must match an existing **option name**
  exactly; the error lists the available options.
- The installation token lasts ~1 hour; generate a fresh one per run (as this sample does).
- Never commit the `.pem`. Prefer a secret store / env var in CI.

## Official references

- Authenticating as a GitHub App — https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app
- Generating a JWT — https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/generating-a-json-web-token-jwt-for-a-github-app
- Installation access token — https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/authenticating-as-a-github-app-installation
- Using the API to manage Projects — https://docs.github.com/en/issues/planning-and-tracking-with-projects/automating-your-project/using-the-api-to-manage-projects
- Create an issue (REST) — https://docs.github.com/en/rest/issues/issues#create-an-issue
