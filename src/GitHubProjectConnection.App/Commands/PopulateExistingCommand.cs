using System.Text.Json;
using GitHubProjectConnection.Options;
using GitHubProjectConnection.Sample;
using GitHubProjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GitHubProjectConnection.Commands;

/// <summary>
/// Goes through every item already in the configured project and sets fresh sample values on
/// each (matching fields by name; unknown fields and bad single-select values are skipped, not
/// fatal). Useful after changing the schema — e.g. re-populating fields that were converted to
/// dropdowns and lost their previous values.
/// </summary>
public sealed class PopulateExistingCommand : ISampleCommand
{
    private readonly IGitHubProjectsClient _projects;
    private readonly TargetOptions _target;
    private readonly ILogger<PopulateExistingCommand> _logger;

    public PopulateExistingCommand(
        IGitHubProjectsClient projects, IOptions<TargetOptions> target, ILogger<PopulateExistingCommand> logger)
    {
        _projects = projects;
        _target = target.Value;
        _logger = logger;
    }

    public string Flag => "--populate-existing";
    public string Description => "Set fresh sample values on every item already in the project.";

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        string projectId = await _projects.GetProjectIdAsync(
            _target.Owner, _target.IsOrganization, _target.ProjectNumber, cancellationToken);
        IReadOnlyDictionary<string, ProjectField> fields =
            await _projects.GetProjectFieldsAsync(projectId, cancellationToken);
        IReadOnlyList<ProjectItem> items =
            await _projects.GetProjectItemsAsync(projectId, cancellationToken);

        _logger.LogInformation("Found {Count} existing item(s) in the project.", items.Count);

        foreach (ProjectItem item in items)
        {
            string label = item.Number is int n ? $"#{n}" : item.Title ?? item.Id;
            _logger.LogInformation("Populating item {Label}...", label);

            // Fresh, valid sample values per item.
            GeneratedIssueData sample = SampleDataGenerator.Generate();

            foreach ((string fieldName, JsonElement value) in sample.Fields)
            {
                if (!fields.TryGetValue(fieldName, out ProjectField? field))
                    continue; // field not in this project

                try
                {
                    await _projects.UpdateFieldValueAsync(projectId, item.Id, field, value, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning("  Could not set '{Field}' on {Label}: {Message}", fieldName, label, ex.Message);
                }
            }
        }

        _logger.LogInformation("Done populating {Count} item(s).", items.Count);
        return 0;
    }
}
