using GitHubProjectConnection.Options;
using GitHubProjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GitHubProjectConnection.Commands;

/// <summary>
/// Demonstrates adding a dropdown option <em>without clearing existing ones</em> via
/// <see cref="IGitHubFieldManager.AddSingleSelectOptionsAsync"/>: it adds a new option (and a
/// duplicate, twice, to show the merge is idempotent and case-insensitive), verifies the existing
/// options survived, then reverts so the field is left as it was found.
/// </summary>
public sealed class AddOptionsDemoCommand : ISampleCommand
{
    private const string FieldName = "Release Confidence Level";
    private const string NewOption = "At Risk (demo)";

    private readonly IGitHubFieldManager _manage;
    private readonly IGitHubProjectsClient _projects;
    private readonly TargetOptions _target;
    private readonly ILogger<AddOptionsDemoCommand> _logger;

    public AddOptionsDemoCommand(
        IGitHubFieldManager manage, IGitHubProjectsClient projects,
        IOptions<TargetOptions> target, ILogger<AddOptionsDemoCommand> logger)
    {
        _manage = manage;
        _projects = projects;
        _target = target.Value;
        _logger = logger;
    }

    public string Flag => "--add-option-demo";
    public string Description => "Add a dropdown option without clearing existing ones (idempotent), then revert.";

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        string projectId = await _projects.GetProjectIdAsync(
            _target.Owner, _target.IsOrganization, _target.ProjectNumber, cancellationToken);

        SingleSelectFieldDetail? before =
            await _projects.GetSingleSelectFieldAsync(projectId, FieldName, cancellationToken);
        if (before is null)
        {
            _logger.LogError("No single-select field named '{Field}' found — cannot demo.", FieldName);
            return 1;
        }

        _logger.LogInformation("BEFORE  options: {Options}", string.Join(", ", before.Options.Select(o => o.Name)));

        // Add a new option plus a duplicate of an existing one (different case) — and do it twice.
        var toAdd = new[]
        {
            new SingleSelectOption(NewOption, "RED"),
            new SingleSelectOption(before.Options[0].Name.ToUpperInvariant(), "GRAY"), // already exists → skipped
        };
        await _manage.AddSingleSelectOptionsAsync(projectId, FieldName, toAdd, cancellationToken);
        ProjectField afterField = await _manage.AddSingleSelectOptionsAsync(projectId, FieldName, toAdd, cancellationToken);
        _logger.LogInformation("AFTER   options: {Options}", string.Join(", ", afterField.Options.Keys));

        SingleSelectFieldDetail after =
            (await _projects.GetSingleSelectFieldAsync(projectId, FieldName, cancellationToken))!;
        bool addedExactlyOnce = after.Options.Count(o => o.Name == NewOption) == 1;
        var afterIds = after.Options.Where(o => o.Id is not null).Select(o => o.Id!).ToHashSet();
        bool originalsKept = before.Options.All(o => o.Id is not null && afterIds.Contains(o.Id!));

        // Revert: write the original option set back (ids preserved) to remove the demo option.
        await _manage.UpdateFieldAsync(before.FieldId, name: null, before.Options, cancellationToken);
        SingleSelectFieldDetail reverted =
            (await _projects.GetSingleSelectFieldAsync(projectId, FieldName, cancellationToken))!;
        _logger.LogInformation("REVERT  options: {Options}", string.Join(", ", reverted.Options.Select(o => o.Name)));

        bool revertedCleanly = reverted.Options.Count == before.Options.Count
            && reverted.Options.All(o => o.Name != NewOption);

        if (addedExactlyOnce && originalsKept && revertedCleanly)
        {
            _logger.LogInformation(
                "DEMO PASSED: added '{New}' once (idempotent), kept all {Count} existing options by id, and reverted.",
                NewOption, before.Options.Count);
            return 0;
        }

        _logger.LogError(
            "DEMO FAILED: addedExactlyOnce={Added}, originalsKept={Kept}, revertedCleanly={Reverted}.",
            addedExactlyOnce, originalsKept, revertedCleanly);
        return 1;
    }
}
