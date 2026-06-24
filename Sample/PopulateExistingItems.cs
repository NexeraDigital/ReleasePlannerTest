using System.Text.Json;
using GitHubProjectConnection.Clients;
using GitHubProjectConnection.Options;
using Microsoft.Extensions.Logging;

namespace GitHubProjectConnection.Sample;

/// <summary>
/// Goes through every item already in the configured project and sets fresh sample values on
/// each (matching fields by name; unknown fields and bad single-select values are skipped, not
/// fatal). Useful after changing the schema — e.g. re-populating fields that were converted to
/// dropdowns and lost their previous values.
///
/// Run with: <c>dotnet run -- --populate-existing</c>
/// </summary>
public static class PopulateExistingItems
{
    public static async Task<int> RunAsync(
        GitHubProjectsClient projects, TargetOptions target, ILogger logger, CancellationToken cancellationToken)
    {
        string projectId = await projects.GetProjectIdAsync(
            target.Owner, target.IsOrganization, target.ProjectNumber, cancellationToken);
        IReadOnlyDictionary<string, ProjectField> fields =
            await projects.GetProjectFieldsAsync(projectId, cancellationToken);
        IReadOnlyList<ProjectItem> items =
            await projects.GetProjectItemsAsync(projectId, cancellationToken);

        logger.LogInformation("Found {Count} existing item(s) in the project.", items.Count);

        foreach (ProjectItem item in items)
        {
            string label = item.Number is int n ? $"#{n}" : item.Title ?? item.Id;
            logger.LogInformation("Populating item {Label}...", label);

            // Fresh, valid sample values per item.
            GeneratedIssueData sample = SampleDataGenerator.Generate();

            foreach ((string fieldName, JsonElement value) in sample.Fields)
            {
                if (!fields.TryGetValue(fieldName, out ProjectField? field))
                    continue; // field not in this project

                try
                {
                    await projects.UpdateFieldValueAsync(projectId, item.Id, field, value, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning("  Could not set '{Field}' on {Label}: {Message}", fieldName, label, ex.Message);
                }
            }
        }

        logger.LogInformation("Done populating {Count} item(s).", items.Count);
        return 0;
    }
}
