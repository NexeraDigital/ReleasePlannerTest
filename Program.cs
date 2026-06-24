using System.Net.Http.Headers;
using System.Text.Json;
using GitHubProjectConnection.Auth;
using GitHubProjectConnection.Clients;
using GitHubProjectConnection.Errors;
using GitHubProjectConnection.Options;
using GitHubProjectConnection.Resilience;
using GitHubProjectConnection.Sample;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// ---------------------------------------------------------------------------
// Options: bound from appsettings.json (or env vars, e.g. GitHubApp__PrivateKeyPath)
// and validated up front so misconfiguration fails with a clear message.
// ---------------------------------------------------------------------------
builder.Services.AddOptions<GitHubAppOptions>()
    .Bind(builder.Configuration.GetSection(GitHubAppOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<TargetOptions>()
    .Bind(builder.Configuration.GetSection(TargetOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Caches the installation token across calls for the life of the process.
builder.Services.AddSingleton<InstallationTokenProvider>();

// Typed clients: one base address + User-Agent, each wrapped in the GitHub-aware
// resilience pipeline (retry honoring Retry-After / rate-limit reset, circuit breaker, timeouts).
builder.Services.AddHttpClient<GitHubAppAuthenticator>(ConfigureGitHubClient).AddGitHubResilience();
builder.Services.AddHttpClient<GitHubRestClient>(ConfigureGitHubClient).AddGitHubResilience();
builder.Services.AddHttpClient<GitHubProjectsClient>(ConfigureGitHubClient).AddGitHubResilience();

using IHost host = builder.Build();

ILogger<Program> logger = host.Services.GetRequiredService<ILogger<Program>>();

// Force options validation now (clear error if config is missing/invalid).
TargetOptions target = host.Services.GetRequiredService<IOptions<TargetOptions>>().Value;
_ = host.Services.GetRequiredService<IOptions<GitHubAppOptions>>().Value;

// Ctrl+C requests a graceful cancellation that flows through every API call.
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

GitHubRestClient rest = host.Services.GetRequiredService<GitHubRestClient>();
GitHubProjectsClient projects = host.Services.GetRequiredService<GitHubProjectsClient>();

try
{
    // 1. Generate fresh sample data so repeated runs produce distinct issues/values.
    GeneratedIssueData sample = SampleDataGenerator.Generate();

    // 2. Create the issue (REST).
    CreatedIssue issue = await rest.CreateIssueAsync(target.Owner, target.Repo, sample.Title, sample.Body, cts.Token);
    logger.LogInformation("Created issue #{Number}: {Url}", issue.Number, issue.HtmlUrl);

    // 3. Resolve the project and read its custom fields (GraphQL).
    string projectId = await projects.GetProjectIdAsync(target.Owner, target.IsOrganization, target.ProjectNumber, cts.Token);
    IReadOnlyDictionary<string, ProjectField> fields = await projects.GetProjectFieldsAsync(projectId, cts.Token);
    logger.LogInformation("Resolved project {ProjectId} with {Count} fields.", projectId, fields.Count);

    // 4. Add the issue to the project.
    string itemId = await projects.AddItemToProjectAsync(projectId, issue.NodeId, cts.Token);
    logger.LogInformation("Added issue to project as item {ItemId}.", itemId);

    // 5. Populate each sample field; a bad single value should not abort the whole run.
    foreach ((string fieldName, JsonElement value) in sample.Fields)
    {
        if (!fields.TryGetValue(fieldName, out ProjectField? field))
        {
            logger.LogWarning("Skipping '{Field}' — no such field in the project.", fieldName);
            continue;
        }

        try
        {
            await projects.UpdateFieldValueAsync(projectId, itemId, field, value, cts.Token);
            logger.LogInformation("Set '{Field}' = {Value}", fieldName, value);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Could not set '{Field}': {Message}", fieldName, ex.Message);
        }
    }

    logger.LogInformation("Done.");
    return 0;
}
catch (OperationCanceledException)
{
    logger.LogWarning("Cancelled.");
    return 130;
}
catch (GitHubApiException ex)
{
    logger.LogError("{Message}", ex.Message);
    return 1;
}

static void ConfigureGitHubClient(HttpClient client)
{
    client.BaseAddress = new Uri("https://api.github.com/");
    // GitHub requires a User-Agent header on every request.
    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GitHubProjectConnection", "1.0"));
}
