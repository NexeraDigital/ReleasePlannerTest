using System.Text.Json;

namespace GitHubProjects;

/// <summary>Creates issues via the GitHub REST API as the authenticated installation.</summary>
public interface IGitHubIssueClient
{
    /// <summary>Creates an issue (<c>POST /repos/{owner}/{repo}/issues</c>) and returns its node id and number.</summary>
    Task<CreatedIssue> CreateIssueAsync(
        string owner, string repo, string title, string? body, CancellationToken cancellationToken);
}

/// <summary>Reads and writes Projects V2 boards and their items (GraphQL).</summary>
public interface IGitHubProjectsClient
{
    /// <summary>Resolves a project's node id from the owner login + project number.</summary>
    Task<string> GetProjectIdAsync(
        string owner, bool isOrganization, int projectNumber, CancellationToken cancellationToken);

    /// <summary>Reads all custom fields (and single-select option ids) for a project, by field name.</summary>
    Task<IReadOnlyDictionary<string, ProjectField>> GetProjectFieldsAsync(
        string projectId, CancellationToken cancellationToken);

    /// <summary>Reads a single-select field's full option details (id, name, color, description), or null.</summary>
    Task<SingleSelectFieldDetail?> GetSingleSelectFieldAsync(
        string projectId, string fieldName, CancellationToken cancellationToken);

    /// <summary>Lists every item already in a project (item id + linked issue/PR number and title).</summary>
    Task<IReadOnlyList<ProjectItem>> GetProjectItemsAsync(
        string projectId, CancellationToken cancellationToken);

    /// <summary>Adds an issue/PR (by content node id) to a project. Returns the project item id.</summary>
    Task<string> AddItemToProjectAsync(
        string projectId, string contentNodeId, CancellationToken cancellationToken);

    /// <summary>Sets one custom field value on a project item (shape depends on the field's data type).</summary>
    Task UpdateFieldValueAsync(
        string projectId, string itemId, ProjectField field, JsonElement rawValue, CancellationToken cancellationToken);
}

/// <summary>Manages Projects V2 custom-field definitions: create, update, and delete (GraphQL).</summary>
public interface IGitHubFieldManager
{
    /// <summary>Creates a custom field (TEXT, NUMBER, DATE, or SINGLE_SELECT).</summary>
    Task<ProjectField> CreateFieldAsync(
        string projectId, string dataType, string name,
        IReadOnlyList<SingleSelectOption>? options, CancellationToken cancellationToken);

    /// <summary>Renames a field and/or replaces its single-select option set (include ids to preserve).</summary>
    Task<ProjectField> UpdateFieldAsync(
        string fieldId, string? name,
        IReadOnlyList<SingleSelectOption>? options, CancellationToken cancellationToken);

    /// <summary>Deletes a custom field (removes the field and all its values across items).</summary>
    Task DeleteFieldAsync(string fieldId, CancellationToken cancellationToken);

    /// <summary>
    /// Adds options to an existing single-select field <strong>without clearing the current ones</strong>.
    /// Fetches the field's existing options, appends any of <paramref name="newOptions"/> whose name
    /// isn't already present (case-insensitive), and writes the merged set back — existing options keep
    /// their id, so item values already assigned to them are preserved. Returns the field's resulting
    /// options. No mutation is sent if every requested option already exists.
    /// </summary>
    Task<ProjectField> AddSingleSelectOptionsAsync(
        string projectId, string fieldName, IReadOnlyList<SingleSelectOption> newOptions, CancellationToken cancellationToken);
}
