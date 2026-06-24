using GitHubProjectConnection.Options;
using GitHubProjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GitHubProjectConnection.Commands;

/// <summary>
/// Converts selected TEXT custom fields into single-select dropdowns. GitHub can't change a
/// field's type in place, so each field is deleted and recreated as a SINGLE_SELECT with the
/// same name (existing text values on that field are lost).
/// </summary>
public sealed class ConvertDropdownsCommand : ISampleCommand
{
    // Fields shown as dropdowns (chevron) in the Launch Plan form, with sample option sets.
    private static readonly (string Field, SingleSelectOption[] Options)[] Targets =
    {
        ("Organization", new[]
        {
            new SingleSelectOption("Azure", "BLUE"),
            new SingleSelectOption("Microsoft 365", "ORANGE"),
            new SingleSelectOption("Security", "RED"),
            new SingleSelectOption("Developer Division", "PURPLE"),
        }),
        ("Product Grouping", new[]
        {
            new SingleSelectOption("Compute", "BLUE"),
            new SingleSelectOption("Storage", "GREEN"),
            new SingleSelectOption("Networking", "PURPLE"),
            new SingleSelectOption("AI Platform", "PINK"),
            new SingleSelectOption("Identity", "ORANGE"),
        }),
        ("Scenarios", new[]
        {
            new SingleSelectOption("New Feature", "GREEN"),
            new SingleSelectOption("Enhancement", "BLUE"),
            new SingleSelectOption("Deprecation", "RED"),
            new SingleSelectOption("Migration", "YELLOW"),
        }),
        ("Roadmap Visibility Level", new[]
        {
            new SingleSelectOption("Public", "GREEN"),
            new SingleSelectOption("Internal", "YELLOW"),
            new SingleSelectOption("Microsoft Confidential", "RED"),
        }),
        ("Release Confidence Level", new[]
        {
            new SingleSelectOption("Low", "GRAY"),
            new SingleSelectOption("Medium", "YELLOW"),
            new SingleSelectOption("High", "GREEN"),
            new SingleSelectOption("Committed", "BLUE"),
        }),
    };

    private readonly IGitHubFieldManager _manage;
    private readonly IGitHubProjectsClient _projects;
    private readonly TargetOptions _target;
    private readonly ILogger<ConvertDropdownsCommand> _logger;

    public ConvertDropdownsCommand(
        IGitHubFieldManager manage, IGitHubProjectsClient projects,
        IOptions<TargetOptions> target, ILogger<ConvertDropdownsCommand> logger)
    {
        _manage = manage;
        _projects = projects;
        _target = target.Value;
        _logger = logger;
    }

    public string Flag => "--convert-dropdowns";
    public string Description => "Convert selected TEXT fields into dropdowns (destructive: delete + recreate).";

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        string projectId = await _projects.GetProjectIdAsync(
            _target.Owner, _target.IsOrganization, _target.ProjectNumber, cancellationToken);
        IReadOnlyDictionary<string, ProjectField> fields =
            await _projects.GetProjectFieldsAsync(projectId, cancellationToken);

        foreach ((string name, SingleSelectOption[] options) in Targets)
        {
            if (!fields.TryGetValue(name, out ProjectField? existing))
            {
                _logger.LogWarning("Field '{Name}' not found — skipping.", name);
                continue;
            }

            if (existing.DataType == "SINGLE_SELECT")
            {
                _logger.LogInformation("Field '{Name}' is already a dropdown — skipping.", name);
                continue;
            }

            // Delete the TEXT field, then recreate it with the same name as a single-select.
            await _manage.DeleteFieldAsync(existing.Id, cancellationToken);
            ProjectField created = await _manage.CreateFieldAsync(
                projectId, "SINGLE_SELECT", name, options, cancellationToken);

            _logger.LogInformation(
                "Converted '{Name}' to dropdown with options: {Options}",
                name, string.Join(", ", created.Options.Keys));
        }

        _logger.LogInformation("Dropdown conversion complete.");
        return 0;
    }
}
