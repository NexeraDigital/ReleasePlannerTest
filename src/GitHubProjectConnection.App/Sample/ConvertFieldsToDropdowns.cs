using GitHubProjectConnection.Options;
using GitHubProjects;
using Microsoft.Extensions.Logging;

namespace GitHubProjectConnection.Sample;

/// <summary>
/// One-off utility: converts selected TEXT custom fields into single-select dropdowns.
/// GitHub can't change a field's type in place, so each field is deleted and recreated as a
/// SINGLE_SELECT with the same name (existing text values on that field are lost).
///
/// Run with: <c>dotnet run -- --convert-dropdowns</c>
/// </summary>
public static class ConvertFieldsToDropdowns
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

    public static async Task<int> RunAsync(
        IGitHubFieldManager manage, IGitHubProjectsClient projects, TargetOptions target,
        ILogger logger, CancellationToken cancellationToken)
    {
        string projectId = await projects.GetProjectIdAsync(
            target.Owner, target.IsOrganization, target.ProjectNumber, cancellationToken);
        IReadOnlyDictionary<string, ProjectField> fields =
            await projects.GetProjectFieldsAsync(projectId, cancellationToken);

        foreach ((string name, SingleSelectOption[] options) in Targets)
        {
            if (!fields.TryGetValue(name, out ProjectField? existing))
            {
                logger.LogWarning("Field '{Name}' not found — skipping.", name);
                continue;
            }

            if (existing.DataType == "SINGLE_SELECT")
            {
                logger.LogInformation("Field '{Name}' is already a dropdown — skipping.", name);
                continue;
            }

            // Delete the TEXT field, then recreate it with the same name as a single-select.
            await manage.DeleteFieldAsync(existing.Id, cancellationToken);
            ProjectField created = await manage.CreateFieldAsync(
                projectId, "SINGLE_SELECT", name, options, cancellationToken);

            logger.LogInformation(
                "Converted '{Name}' to dropdown with options: {Options}",
                name, string.Join(", ", created.Options.Keys));
        }

        logger.LogInformation("Dropdown conversion complete.");
        return 0;
    }
}
