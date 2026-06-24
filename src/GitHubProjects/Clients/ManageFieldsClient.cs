using System.Text.Json;

namespace GitHubProjects;

/// <summary>
/// Manages Projects V2 custom-field <em>definitions</em> (not values) via GraphQL:
/// create, update (rename / edit single-select options non-destructively), and delete.
/// Requires the App's Projects: Read and write permission.
/// </summary>
internal sealed class ManageFieldsClient : GitHubClientBase, IGitHubFieldManager
{
    public ManageFieldsClient(HttpClient http, InstallationTokenProvider tokenProvider)
        : base(http, tokenProvider)
    {
    }

    /// <summary>
    /// Creates a custom field. <paramref name="dataType"/> is one of TEXT, NUMBER, DATE, SINGLE_SELECT;
    /// single-select fields require <paramref name="options"/>.
    /// </summary>
    public async Task<ProjectField> CreateFieldAsync(
        string projectId, string dataType, string name,
        IReadOnlyList<SingleSelectOption>? options, CancellationToken cancellationToken)
    {
        const string mutation = """
            mutation($input: CreateProjectV2FieldInput!) {
              createProjectV2Field(input: $input) {
                projectV2Field {
                  ... on ProjectV2FieldCommon { id name dataType }
                  ... on ProjectV2SingleSelectField { options { id name } }
                }
              }
            }
            """;

        Dictionary<string, object?> input = ManageFieldInputs.CreateField(projectId, dataType, name, options);
        JsonElement data = await GraphQLAsync(mutation, new { input }, "create project field", cancellationToken);
        return ParseField(data.GetProperty("createProjectV2Field").GetProperty("projectV2Field"));
    }

    /// <summary>
    /// Renames a field and/or replaces its single-select option set. Pass <paramref name="options"/>
    /// as the FULL desired list, including each existing option's <see cref="SingleSelectOption.Id"/>
    /// to preserve it (and the values already assigned to it). Omitted options are removed.
    /// </summary>
    public async Task<ProjectField> UpdateFieldAsync(
        string fieldId, string? name,
        IReadOnlyList<SingleSelectOption>? options, CancellationToken cancellationToken)
    {
        const string mutation = """
            mutation($input: UpdateProjectV2FieldInput!) {
              updateProjectV2Field(input: $input) {
                projectV2Field {
                  ... on ProjectV2FieldCommon { id name dataType }
                  ... on ProjectV2SingleSelectField { options { id name } }
                }
              }
            }
            """;

        Dictionary<string, object?> input = ManageFieldInputs.UpdateField(fieldId, name, options);
        JsonElement data = await GraphQLAsync(mutation, new { input }, "update project field", cancellationToken);
        return ParseField(data.GetProperty("updateProjectV2Field").GetProperty("projectV2Field"));
    }

    /// <summary>Deletes a custom field. This removes the field and all its values across items.</summary>
    public async Task DeleteFieldAsync(string fieldId, CancellationToken cancellationToken)
    {
        const string mutation = """
            mutation($input: DeleteProjectV2FieldInput!) {
              deleteProjectV2Field(input: $input) { clientMutationId }
            }
            """;

        await GraphQLAsync(
            mutation, new { input = ManageFieldInputs.DeleteField(fieldId) }, "delete project field", cancellationToken);
    }

    private static ProjectField ParseField(JsonElement node)
    {
        string id = node.GetProperty("id").GetString()!;
        string name = node.GetProperty("name").GetString()!;
        string dataType = node.GetProperty("dataType").GetString()!;

        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (node.TryGetProperty("options", out JsonElement opts) && opts.ValueKind == JsonValueKind.Array)
            foreach (JsonElement opt in opts.EnumerateArray())
                options[opt.GetProperty("name").GetString()!] = opt.GetProperty("id").GetString()!;

        return new ProjectField(id, name, dataType, options);
    }
}
