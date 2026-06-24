using GitHubProjectConnection.Options;
using GitHubProjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GitHubProjectConnection.Commands;

/// <summary>
/// Validates that an existing single-select dropdown can be modified non-destructively:
/// read its options, add a new one while keeping the existing options (by id, color, and
/// description), confirm the change, then revert to the original option set.
/// </summary>
public sealed class ValidateDropdownCommand : ISampleCommand
{
    private const string FieldName = "Release Confidence Level";
    private const string TempOption = "Validation (temp)";

    private readonly IGitHubProjectsClient _projects;
    private readonly IGitHubFieldManager _manage;
    private readonly TargetOptions _target;
    private readonly ILogger<ValidateDropdownCommand> _logger;

    public ValidateDropdownCommand(
        IGitHubProjectsClient projects, IGitHubFieldManager manage,
        IOptions<TargetOptions> target, ILogger<ValidateDropdownCommand> logger)
    {
        _projects = projects;
        _manage = manage;
        _target = target.Value;
        _logger = logger;
    }

    public string Flag => "--validate-dropdown";
    public string Description => $"Prove '{FieldName}' can be modified non-destructively, then revert.";

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        string projectId = await _projects.GetProjectIdAsync(
            _target.Owner, _target.IsOrganization, _target.ProjectNumber, cancellationToken);

        SingleSelectFieldDetail? before =
            await _projects.GetSingleSelectFieldAsync(projectId, FieldName, cancellationToken);
        if (before is null)
        {
            _logger.LogError("No single-select field named '{Field}' found — cannot validate.", FieldName);
            return 1;
        }

        _logger.LogInformation("BEFORE  '{Field}' ({Id}) options: {Options}",
            before.Name, before.FieldId, Describe(before.Options));

        // --- Modify: keep every existing option (id+color+description), add one new option. ---
        var modified = before.Options
            .Append(new SingleSelectOption(TempOption, "PURPLE", "Added by validation"))
            .ToArray();
        await _manage.UpdateFieldAsync(before.FieldId, name: null, modified, cancellationToken);

        SingleSelectFieldDetail after =
            (await _projects.GetSingleSelectFieldAsync(projectId, FieldName, cancellationToken))!;
        _logger.LogInformation("AFTER   options: {Options}", Describe(after.Options));

        // --- Assert non-destructive: new option present, all original ids preserved. ---
        bool addedPresent = after.Options.Any(o => o.Name == TempOption);
        var afterIds = after.Options.Where(o => o.Id is not null).Select(o => o.Id!).ToHashSet();
        bool originalsKept = before.Options.All(o => o.Id is not null && afterIds.Contains(o.Id!));

        // --- Revert to the original option set (ids preserved). ---
        await _manage.UpdateFieldAsync(before.FieldId, name: null, before.Options, cancellationToken);
        SingleSelectFieldDetail reverted =
            (await _projects.GetSingleSelectFieldAsync(projectId, FieldName, cancellationToken))!;
        _logger.LogInformation("REVERT  options: {Options}", Describe(reverted.Options));

        bool revertedCleanly = reverted.Options.Count == before.Options.Count
            && reverted.Options.All(o => o.Name != TempOption);

        if (addedPresent && originalsKept && revertedCleanly)
        {
            _logger.LogInformation(
                "VALIDATION PASSED: added an option, preserved all {Count} existing options by id, and reverted cleanly.",
                before.Options.Count);
            return 0;
        }

        _logger.LogError(
            "VALIDATION FAILED: addedPresent={Added}, originalsKept={Kept}, revertedCleanly={Reverted}.",
            addedPresent, originalsKept, revertedCleanly);
        return 1;
    }

    private static string Describe(IReadOnlyList<SingleSelectOption> options) =>
        string.Join(", ", options.Select(o => $"{o.Name}({o.Color})"));
}
