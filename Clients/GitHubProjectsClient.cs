using System.Net.Http.Json;
using System.Text.Json;
using GitHubProjectConnection.Auth;
using GitHubProjectConnection.Errors;

namespace GitHubProjectConnection.Clients;

/// <summary>
/// Talks to GitHub's Projects V2 API, which is GraphQL-only: resolve a project, read its
/// custom fields (paginated), add an item, and set field values.
/// </summary>
public sealed class GitHubProjectsClient : GitHubClientBase
{
    private const int FieldPageSize = 50;

    public GitHubProjectsClient(HttpClient http, InstallationTokenProvider tokenProvider)
        : base(http, tokenProvider)
    {
    }

    /// <summary>Resolves a project's node id from the owner login + project number.</summary>
    public async Task<string> GetProjectIdAsync(
        string owner, bool isOrganization, int projectNumber, CancellationToken cancellationToken)
    {
        string root = isOrganization ? "organization" : "user";
        string query = $$"""
            query($login: String!, $number: Int!) {
              {{root}}(login: $login) {
                projectV2(number: $number) { id }
              }
            }
            """;

        JsonElement data = await GraphQLAsync(query, new { login = owner, number = projectNumber }, "resolve project", cancellationToken);
        JsonElement project = data.GetProperty(root).GetProperty("projectV2");
        if (project.ValueKind == JsonValueKind.Null)
            throw new GitHubApiException(
                $"Project #{projectNumber} not found for {root} '{owner}'. " +
                "Check the number and that the App has Projects access.");
        return project.GetProperty("id").GetString()!;
    }

    /// <summary>Reads all custom fields (and single-select option ids) for a project, paging as needed.</summary>
    public async Task<IReadOnlyDictionary<string, ProjectField>> GetProjectFieldsAsync(
        string projectId, CancellationToken cancellationToken)
    {
        const string query = """
            query($projectId: ID!, $pageSize: Int!, $after: String) {
              node(id: $projectId) {
                ... on ProjectV2 {
                  fields(first: $pageSize, after: $after) {
                    pageInfo { hasNextPage endCursor }
                    nodes {
                      ... on ProjectV2FieldCommon { id name dataType }
                      ... on ProjectV2SingleSelectField {
                        id name dataType
                        options { id name }
                      }
                    }
                  }
                }
              }
            }
            """;

        var fields = new Dictionary<string, ProjectField>(StringComparer.OrdinalIgnoreCase);
        string? after = null;

        do
        {
            JsonElement data = await GraphQLAsync(
                query, new { projectId, pageSize = FieldPageSize, after }, "read project fields", cancellationToken);

            JsonElement fieldsConn = data.GetProperty("node").GetProperty("fields");
            foreach (JsonElement node in fieldsConn.GetProperty("nodes").EnumerateArray())
            {
                if (!node.TryGetProperty("id", out _)) continue; // skip unsupported field unions

                string id = node.GetProperty("id").GetString()!;
                string name = node.GetProperty("name").GetString()!;
                string dataType = node.GetProperty("dataType").GetString()!;

                var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (node.TryGetProperty("options", out JsonElement opts) && opts.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement opt in opts.EnumerateArray())
                        options[opt.GetProperty("name").GetString()!] = opt.GetProperty("id").GetString()!;

                fields[name] = new ProjectField(id, name, dataType, options);
            }

            JsonElement pageInfo = fieldsConn.GetProperty("pageInfo");
            after = pageInfo.GetProperty("hasNextPage").GetBoolean()
                ? pageInfo.GetProperty("endCursor").GetString()
                : null;
        }
        while (after is not null);

        return fields;
    }

    /// <summary>Adds an issue/PR (by content node id) to a project. Returns the project item id.</summary>
    public async Task<string> AddItemToProjectAsync(
        string projectId, string contentNodeId, CancellationToken cancellationToken)
    {
        const string mutation = """
            mutation($projectId: ID!, $contentId: ID!) {
              addProjectV2ItemById(input: { projectId: $projectId, contentId: $contentId }) {
                item { id }
              }
            }
            """;

        JsonElement data = await GraphQLAsync(
            mutation, new { projectId, contentId = contentNodeId }, "add item to project", cancellationToken);
        return data.GetProperty("addProjectV2ItemById").GetProperty("item").GetProperty("id").GetString()!;
    }

    /// <summary>
    /// Sets one custom field on a project item. The GraphQL value shape depends on the field's
    /// data type (text / number / date / single-select); see <see cref="ProjectFieldValue"/>.
    /// </summary>
    public async Task UpdateFieldValueAsync(
        string projectId, string itemId, ProjectField field, JsonElement rawValue, CancellationToken cancellationToken)
    {
        object value = ProjectFieldValue.Build(field, rawValue);

        const string mutation = """
            mutation($projectId: ID!, $itemId: ID!, $fieldId: ID!, $value: ProjectV2FieldValue!) {
              updateProjectV2ItemFieldValue(
                input: { projectId: $projectId, itemId: $itemId, fieldId: $fieldId, value: $value }
              ) {
                projectV2Item { id }
              }
            }
            """;

        await GraphQLAsync(
            mutation, new { projectId, itemId, fieldId = field.Id, value }, "set field value", cancellationToken);
    }

    /// <summary>Posts a GraphQL query/mutation and returns the "data" element, throwing on GraphQL errors.</summary>
    private async Task<JsonElement> GraphQLAsync(
        string query, object variables, string operation, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "graphql")
        {
            Content = JsonContent.Create(new { query, variables })
        };
        await ApplyHeadersAsync(request, cancellationToken);

        using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, operation, cancellationToken);

        JsonElement payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        if (payload.TryGetProperty("errors", out JsonElement errors) &&
            errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            throw GitHubApiException.FromGraphQL(operation, errors);

        return payload.GetProperty("data");
    }
}
