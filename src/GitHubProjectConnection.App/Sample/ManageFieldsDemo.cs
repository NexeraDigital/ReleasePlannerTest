using GitHubProjectConnection.Options;
using GitHubProjects;
using Microsoft.Extensions.Logging;

namespace GitHubProjectConnection.Sample;

/// <summary>
/// Demonstrates managing custom-field <em>definitions</em> end to end against the configured
/// project: create a single-select field, update it non-destructively (rename + keep existing
/// options by id + add a new one), then delete it so nothing is left behind.
///
/// Run with: <c>dotnet run -- --manage-fields-demo</c>
/// </summary>
public static class ManageFieldsDemo
{
    public static async Task<int> RunAsync(
        IGitHubFieldManager manage, IGitHubProjectsClient projects, TargetOptions target,
        ILogger logger, CancellationToken cancellationToken)
    {
        string projectId = await projects.GetProjectIdAsync(
            target.Owner, target.IsOrganization, target.ProjectNumber, cancellationToken);

        // 1. Create a single-select field with three options.
        ProjectField created = await manage.CreateFieldAsync(
            projectId, "SINGLE_SELECT", "Demo Priority (sample)",
            new[]
            {
                new SingleSelectOption("Low", "GRAY"),
                new SingleSelectOption("Medium", "YELLOW"),
                new SingleSelectOption("High", "RED"),
            },
            cancellationToken);
        logger.LogInformation(
            "Created field '{Name}' ({Id}) with options: {Options}",
            created.Name, created.Id, string.Join(", ", created.Options.Keys));

        // 2. Update non-destructively: rename, KEEP the existing options (by id), and ADD one.
        var keptPlusNew = created.Options
            .Select(o => new SingleSelectOption(o.Key, "GRAY", Id: o.Value))   // o.Value = existing option id
            .Append(new SingleSelectOption("Critical", "PURPLE"))              // new option (no id)
            .ToArray();

        ProjectField updated = await manage.UpdateFieldAsync(
            created.Id, "Demo Priority renamed (sample)", keptPlusNew, cancellationToken);
        logger.LogInformation(
            "Updated field -> '{Name}' with options: {Options}",
            updated.Name, string.Join(", ", updated.Options.Keys));

        // 3. Clean up so the demo leaves the project as it found it.
        await manage.DeleteFieldAsync(created.Id, cancellationToken);
        logger.LogInformation("Deleted field {Id}. Demo complete.", created.Id);

        return 0;
    }
}
