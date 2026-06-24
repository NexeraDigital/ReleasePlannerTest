using System.Text.Json;
using GitHubProjectConnection.Options;
using GitHubProjectConnection.Sample;
using GitHubProjects;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Pin the content root to the binary's folder so appsettings.json (copied to the output
// directory) loads no matter which directory `dotnet run` is invoked from.
HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// App-specific target (owner/repo/project) — bound from the "Target" section, validated on start.
builder.Services.AddOptions<TargetOptions>()
    .Bind(builder.Configuration.GetSection(TargetOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Register the reusable GitHub Projects library (App auth + typed clients + resilience).
builder.Services.AddGitHubProjects(builder.Configuration.GetSection(GitHubAppOptions.SectionName));

// Bridge the installation owner from the "Target" section into the library's app options, so the
// token provider can auto-discover the installation id when GitHubApp:InstallationId isn't set.
string? owner = builder.Configuration["Target:Owner"];
bool ownerIsOrg = !string.Equals(builder.Configuration["Target:OwnerType"], "User", StringComparison.OrdinalIgnoreCase);
builder.Services.Configure<GitHubAppOptions>(o =>
{
    o.Owner ??= owner;
    o.OwnerIsOrganization = ownerIsOrg;
});

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

IGitHubIssueClient issues = host.Services.GetRequiredService<IGitHubIssueClient>();
IGitHubProjectsClient projects = host.Services.GetRequiredService<IGitHubProjectsClient>();

try
{
    // Optional commands operate on the configured project and exit. See README "Managing custom fields".
    if (args.Contains("--manage-fields-demo"))
    {
        IGitHubFieldManager manage = host.Services.GetRequiredService<IGitHubFieldManager>();
        return await ManageFieldsDemo.RunAsync(manage, projects, target, logger, cts.Token);
    }

    if (args.Contains("--convert-dropdowns"))
    {
        IGitHubFieldManager manage = host.Services.GetRequiredService<IGitHubFieldManager>();
        return await ConvertFieldsToDropdowns.RunAsync(manage, projects, target, logger, cts.Token);
    }

    if (args.Contains("--populate-existing"))
    {
        return await PopulateExistingItems.RunAsync(projects, target, logger, cts.Token);
    }

    if (args.Contains("--validate-dropdown"))
    {
        IGitHubFieldManager manage = host.Services.GetRequiredService<IGitHubFieldManager>();
        return await ValidateDropdown.RunAsync(projects, manage, target, logger, cts.Token);
    }

    // Default flow: create an issue, add it to the project, and populate fields with sample data.
    GeneratedIssueData sample = SampleDataGenerator.Generate();

    CreatedIssue issue = await issues.CreateIssueAsync(target.Owner, target.Repo, sample.Title, sample.Body, cts.Token);
    logger.LogInformation("Created issue #{Number}: {Url}", issue.Number, issue.HtmlUrl);

    string projectId = await projects.GetProjectIdAsync(target.Owner, target.IsOrganization, target.ProjectNumber, cts.Token);
    IReadOnlyDictionary<string, ProjectField> fields = await projects.GetProjectFieldsAsync(projectId, cts.Token);
    logger.LogInformation("Resolved project {ProjectId} with {Count} fields.", projectId, fields.Count);

    string itemId = await projects.AddItemToProjectAsync(projectId, issue.NodeId, cts.Token);
    logger.LogInformation("Added issue to project as item {ItemId}.", itemId);

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
