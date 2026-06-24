# GitHub Project Connection (C#)

A production-shaped C# example that authenticates as a **GitHub App**, then:

1. Creates a new **issue** (REST).
2. Adds it to a **Project (Projects V2)** and **populates custom fields** (GraphQL).

Projects V2 is **GraphQL-only** — there is no REST API for project fields, which is why
the project parts of this sample use GraphQL while issue creation uses REST.

It is built on the patterns the .NET team recommends for real services: the **Generic Host**
with dependency injection, **typed `HttpClient`s** via `IHttpClientFactory`, the **standard
resilience pipeline** (retry + circuit breaker + timeout) customized to honor GitHub's
rate-limit rules, strongly-typed **validated options**, structured **logging**, **cancellation**,
and **unit tests** + **CI**.

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

### Project layout

```
Program.cs                       Host bootstrap (DI, options, typed clients) + orchestration
Options/                         GitHubAppOptions, TargetOptions (validated on startup)
Auth/
  GitHubAppAuthenticator.cs      App JWT + installation-token calls
  InstallationTokenProvider.cs   Caches the token until shortly before it expires
Clients/
  GitHubClientBase.cs            Shared auth headers + error handling
  GitHubRestClient.cs            Issue creation (REST)
  GitHubProjectsClient.cs        Projects V2 (GraphQL), with field pagination
  ProjectModels.cs               Records + the (unit-tested) field-value shaping
Resilience/
  GitHubResilience.cs            AddStandardResilienceHandler wiring
  GitHubRateLimit.cs             Retry-After / x-ratelimit-reset interpretation (unit-tested)
Errors/GitHubApiException.cs     Typed errors parsed from REST/GraphQL bodies
Sample/SampleDataGenerator.cs    Randomized sample data
tests/                           xUnit tests (JWT, field shaping, errors, rate-limit)
```

### Resilience & rate limits

Every GitHub call goes through the .NET standard resilience handler. GitHub asks API clients to
respect the `Retry-After` header and, when `x-ratelimit-remaining` hits 0, to wait until
`x-ratelimit-reset`. The stock handler only does blind exponential backoff, so `GitHubRateLimit`
teaches the retry stage to read those headers (see
[GitHub REST best practices](https://docs.github.com/en/rest/using-the-rest-api/best-practices-for-using-the-rest-api)).
Requests are issued serially, in line with GitHub's secondary-rate-limit guidance.

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
  `github.com/orgs/<owner>/projects/<ProjectNumber>`. `OwnerType` is `Organization` or `User`.

Configuration is **validated on startup** (DataAnnotations) — a missing or malformed value
fails fast with a clear message rather than a `NullReferenceException` mid-run.

The issue and custom-field values are generated per run by `SampleDataGenerator.cs`
(not configured in `appsettings.json`).

#### Supplying the private key as a secret

Any value can be supplied via environment variables (`Section__Key`), e.g.
`GitHubApp__PrivateKeyPath=/secure/key.pem`. For CI or a deployed service, prefer passing the
**PEM contents directly** from a secret store (Key Vault, GitHub Actions secret) instead of a
file on disk:

```bash
export GitHubApp__PrivateKeyPem="$(cat key.pem)"   # takes precedence over PrivateKeyPath
```

## Run

```bash
dotnet run
```

Each run creates a new **sample** issue (see [Sample data](#sample-data)). Expected output
(logged via `ILogger`):

```
info: GitHubProjectConnection.Auth.InstallationTokenProvider[0]
      Using installation id 12345678.
info: GitHubProjectConnection.Auth.InstallationTokenProvider[0]
      Obtained installation access token (expires 2026-06-24 16:30:45Z).
info: Program[0]
      Created issue #42: https://github.com/my-org/my-repo/issues/42
info: Program[0]
      Resolved project PVT_xxx with 12 fields.
info: Program[0]
      Added issue to project as item PVTI_xxx.
info: Program[0]
      Set 'Status' = "In Progress"
info: Program[0]
      Done.
```

## Test

```bash
dotnet test
```

Unit tests cover the pure logic without hitting the network: JWT construction (claims +
signature), field-value shaping per data type, single-select option resolution, REST/GraphQL
error parsing, and the rate-limit retry-delay calculation.

## Notes & troubleshooting

- **`Resource not accessible by integration`** → the App is missing the Projects (or Issues)
  permission, or isn't installed on the owner. Re-grant permissions and accept them.
- **Project not found** → wrong `ProjectNumber`/`OwnerType`, or the project belongs to a
  different owner than the repo. Org projects use `OwnerType: organization`; personal
  projects use `user`.
- **Single-select option not found** → the value must match an existing **option name**
  exactly; the error lists the available options.
- The installation token lasts ~1 hour; `InstallationTokenProvider` caches it and refreshes
  shortly before expiry.
- Never commit the `.pem`. Prefer a secret store / env var in CI (see above).

## Where to go next

This sample stops at single-process resilience. For a long-running service you would likely add:
distributed tracing/metrics (OpenTelemetry — the HTTP + resilience pipelines are already
instrumented), tuned circuit-breaker thresholds, and idempotency guards if the operation must
not double-create on retry.

## Official references

- Authenticating as a GitHub App — https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app
- Generating a JWT — https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/generating-a-json-web-token-jwt-for-a-github-app
- Installation access token — https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/authenticating-as-a-github-app-installation
- Using the API to manage Projects — https://docs.github.com/en/issues/planning-and-tracking-with-projects/automating-your-project/using-the-api-to-manage-projects
- Create an issue (REST) — https://docs.github.com/en/rest/issues/issues#create-an-issue
