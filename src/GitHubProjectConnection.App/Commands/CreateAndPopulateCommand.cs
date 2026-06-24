using System.Text.Json;
using GitHubProjectConnection.Options;
using GitHubProjectConnection.Sample;
using GitHubProjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GitHubProjectConnection.Commands;

/// <summary>
/// Default command: create a sample issue, add it to the project, and populate its custom
/// fields with generated sample data.
/// </summary>
public sealed class CreateAndPopulateCommand : ISampleCommand
{
    private readonly IGitHubIssueClient _issues;
    private readonly IGitHubProjectsClient _projects;
    private readonly TargetOptions _target;
    private readonly ILogger<CreateAndPopulateCommand> _logger;

    public CreateAndPopulateCommand(
        IGitHubIssueClient issues, IGitHubProjectsClient projects,
        IOptions<TargetOptions> target, ILogger<CreateAndPopulateCommand> logger)
    {
        _issues = issues;
        _projects = projects;
        _target = target.Value;
        _logger = logger;
    }

    public string Flag => ""; // default
    public string Description => "Create a sample issue, add it to the project, and populate its fields.";

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        GeneratedIssueData sample = SampleDataGenerator.Generate();

        CreatedIssue issue = await _issues.CreateIssueAsync(_target.Owner, _target.Repo, sample.Title, sample.Body, cancellationToken);
        _logger.LogInformation("Created issue #{Number}: {Url}", issue.Number, issue.HtmlUrl);

        string projectId = await _projects.GetProjectIdAsync(_target.Owner, _target.IsOrganization, _target.ProjectNumber, cancellationToken);
        IReadOnlyDictionary<string, ProjectField> fields = await _projects.GetProjectFieldsAsync(projectId, cancellationToken);
        _logger.LogInformation("Resolved project {ProjectId} with {Count} fields.", projectId, fields.Count);

        string itemId = await _projects.AddItemToProjectAsync(projectId, issue.NodeId, cancellationToken);
        _logger.LogInformation("Added issue to project as item {ItemId}.", itemId);

        foreach ((string fieldName, JsonElement value) in sample.Fields)
        {
            if (!fields.TryGetValue(fieldName, out ProjectField? field))
            {
                _logger.LogWarning("Skipping '{Field}' — no such field in the project.", fieldName);
                continue;
            }

            try
            {
                await _projects.UpdateFieldValueAsync(projectId, itemId, field, value, cancellationToken);
                _logger.LogInformation("Set '{Field}' = {Value}", fieldName, value);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Could not set '{Field}': {Message}", fieldName, ex.Message);
            }
        }

        _logger.LogInformation("Done.");
        return 0;
    }
}
