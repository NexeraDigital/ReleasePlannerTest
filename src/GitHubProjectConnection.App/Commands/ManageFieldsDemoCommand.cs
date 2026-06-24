using GitHubProjectConnection.Options;
using GitHubProjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GitHubProjectConnection.Commands;

/// <summary>
/// Demonstrates managing custom-field <em>definitions</em> end to end against the configured
/// project: create a single-select field, update it non-destructively (rename + keep existing
/// options by id + add a new one), then delete it so nothing is left behind.
/// </summary>
public sealed class ManageFieldsDemoCommand : ISampleCommand
{
    private readonly IGitHubFieldManager _manage;
    private readonly IGitHubProjectsClient _projects;
    private readonly TargetOptions _target;
    private readonly ILogger<ManageFieldsDemoCommand> _logger;

    public ManageFieldsDemoCommand(
        IGitHubFieldManager manage, IGitHubProjectsClient projects,
        IOptions<TargetOptions> target, ILogger<ManageFieldsDemoCommand> logger)
    {
        _manage = manage;
        _projects = projects;
        _target = target.Value;
        _logger = logger;
    }

    public string Flag => "--manage-fields-demo";
    public string Description => "Self-cleaning demo: create a field, update it non-destructively, then delete it.";

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        string projectId = await _projects.GetProjectIdAsync(
            _target.Owner, _target.IsOrganization, _target.ProjectNumber, cancellationToken);

        // 1. Create a single-select field with three options.
        ProjectField created = await _manage.CreateFieldAsync(
            projectId, "SINGLE_SELECT", "Demo Priority (sample)",
            new[]
            {
                new SingleSelectOption("Low", "GRAY"),
                new SingleSelectOption("Medium", "YELLOW"),
                new SingleSelectOption("High", "RED"),
            },
            cancellationToken);
        _logger.LogInformation(
            "Created field '{Name}' ({Id}) with options: {Options}",
            created.Name, created.Id, string.Join(", ", created.Options.Keys));

        // 2. Update non-destructively: rename, KEEP the existing options (by id), and ADD one.
        var keptPlusNew = created.Options
            .Select(o => new SingleSelectOption(o.Key, "GRAY", Id: o.Value))   // o.Value = existing option id
            .Append(new SingleSelectOption("Critical", "PURPLE"))              // new option (no id)
            .ToArray();

        ProjectField updated = await _manage.UpdateFieldAsync(
            created.Id, "Demo Priority renamed (sample)", keptPlusNew, cancellationToken);
        _logger.LogInformation(
            "Updated field -> '{Name}' with options: {Options}",
            updated.Name, string.Join(", ", updated.Options.Keys));

        // 3. Clean up so the demo leaves the project as it found it.
        await _manage.DeleteFieldAsync(created.Id, cancellationToken);
        _logger.LogInformation("Deleted field {Id}. Demo complete.", created.Id);

        return 0;
    }
}
