using GitHubProjectConnection.Options;
using GitHubProjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GitHubProjectConnection.Commands;

/// <summary>
/// Lists project items changed since yesterday using the server-side <c>updated:&gt;=</c> filter
/// (delta query — no full scan), showing each item's last-updated timestamp and the most recently
/// changed field plus who changed it. Demonstrates <see cref="IGitHubProjectsClient.GetItemsChangedSinceAsync"/>.
/// </summary>
public sealed class ListChangedCommand : ISampleCommand
{
    private readonly IGitHubProjectsClient _projects;
    private readonly TargetOptions _target;
    private readonly ILogger<ListChangedCommand> _logger;

    public ListChangedCommand(
        IGitHubProjectsClient projects, IOptions<TargetOptions> target, ILogger<ListChangedCommand> logger)
    {
        _projects = projects;
        _target = target.Value;
        _logger = logger;
    }

    public string Flag => "--list-changed";
    public string Description => "List items changed since yesterday (server-side updated: filter), with attribution.";

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        string projectId = await _projects.GetProjectIdAsync(
            _target.Owner, _target.IsOrganization, _target.ProjectNumber, cancellationToken);

        // Server filter is date-granularity; ask for "yesterday or later" to include today's changes.
        DateOnly since = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        IReadOnlyList<ProjectItemDetail> items = await _projects.GetItemsChangedSinceAsync(projectId, since, cancellationToken);

        _logger.LogInformation("{Count} item(s) changed since {Since} (server-side updated: filter):", items.Count, since);
        foreach (ProjectItemDetail item in items.OrderByDescending(i => i.UpdatedAt))
        {
            string label = item.Number is int n ? $"#{n}" : item.Title ?? item.Id;

            // The field that changed most recently on this item, and who changed it.
            ProjectItemFieldValue? latest = item.FieldValues
                .Where(v => v.UpdatedAt is not null)
                .OrderByDescending(v => v.UpdatedAt)
                .FirstOrDefault();

            _logger.LogInformation(
                "  {Label,-6} item.updatedAt={Updated:u}  Organization={Org}  latestField={Field}={Val} by {By}",
                label, item.UpdatedAt, item.ValueOf("Organization") ?? "-",
                latest?.FieldName ?? "-", latest?.Value ?? "-", latest?.UpdatedBy ?? "-");
        }

        _logger.LogInformation(
            "Tip: the server filter is per-day; refine to an exact instant by comparing item.updatedAt to your watermark.");
        return 0;
    }
}
