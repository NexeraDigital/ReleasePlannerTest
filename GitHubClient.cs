using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace GitHubProjectConnection;

/// <summary>A Projects V2 custom field, with single-select options if applicable.</summary>
public sealed record ProjectField(string Id, string Name, string DataType, IReadOnlyDictionary<string, string> Options);

/// <summary>The result of creating an issue.</summary>
public sealed record CreatedIssue(string NodeId, int Number, string HtmlUrl);

/// <summary>
/// Talks to GitHub as an authenticated installation: REST for creating issues,
/// GraphQL for everything Projects V2 (which is GraphQL-only).
/// </summary>
public sealed class GitHubClient(HttpClient http, string installationToken)
{
    // ----- REST: create an issue -----------------------------------------------

    /// <summary>POST /repos/{owner}/{repo}/issues — returns the issue's node id and number.</summary>
    public async Task<CreatedIssue> CreateIssueAsync(string owner, string repo, string title, string? body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"repos/{owner}/{repo}/issues")
        {
            Content = JsonContent.Create(new { title, body })
        };
        ApplyHeaders(request);

        using var response = await http.SendAsync(request);
        await EnsureSuccessAsync(response, "create issue");

        JsonElement issue = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new CreatedIssue(
            issue.GetProperty("node_id").GetString()!,
            issue.GetProperty("number").GetInt32(),
            issue.GetProperty("html_url").GetString()!);
    }

    // ----- GraphQL: Projects V2 ------------------------------------------------

    /// <summary>Resolves a project's node id from the owner login + project number.</summary>
    public async Task<string> GetProjectIdAsync(string owner, bool isOrganization, int projectNumber)
    {
        string root = isOrganization ? "organization" : "user";
        string query = $$"""
            query($login: String!, $number: Int!) {
              {{root}}(login: $login) {
                projectV2(number: $number) { id }
              }
            }
            """;

        JsonElement data = await GraphQLAsync(query, new { login = owner, number = projectNumber });
        JsonElement project = data.GetProperty(root).GetProperty("projectV2");
        if (project.ValueKind == JsonValueKind.Null)
            throw new InvalidOperationException(
                $"Project #{projectNumber} not found for {root} '{owner}'. " +
                "Check the number and that the App has Projects access.");
        return project.GetProperty("id").GetString()!;
    }

    /// <summary>Reads all custom fields (and single-select option ids) for a project.</summary>
    public async Task<IReadOnlyDictionary<string, ProjectField>> GetProjectFieldsAsync(string projectId)
    {
        const string query = """
            query($projectId: ID!) {
              node(id: $projectId) {
                ... on ProjectV2 {
                  fields(first: 50) {
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

        JsonElement data = await GraphQLAsync(query, new { projectId });
        var fields = new Dictionary<string, ProjectField>(StringComparer.OrdinalIgnoreCase);

        foreach (JsonElement node in data.GetProperty("node").GetProperty("fields").GetProperty("nodes").EnumerateArray())
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

        return fields;
    }

    /// <summary>Adds an issue/PR (by content node id) to a project. Returns the project item id.</summary>
    public async Task<string> AddItemToProjectAsync(string projectId, string contentNodeId)
    {
        const string mutation = """
            mutation($projectId: ID!, $contentId: ID!) {
              addProjectV2ItemById(input: { projectId: $projectId, contentId: $contentId }) {
                item { id }
              }
            }
            """;

        JsonElement data = await GraphQLAsync(mutation, new { projectId, contentId = contentNodeId });
        return data.GetProperty("addProjectV2ItemById").GetProperty("item").GetProperty("id").GetString()!;
    }

    /// <summary>
    /// Sets one custom field on a project item. The GraphQL value shape depends on the
    /// field's data type (text / number / date / single-select).
    /// </summary>
    public async Task UpdateFieldValueAsync(string projectId, string itemId, ProjectField field, JsonElement rawValue)
    {
        object value = field.DataType switch
        {
            "TEXT" => new { text = rawValue.ValueKind == JsonValueKind.String ? rawValue.GetString() : rawValue.ToString() },
            "NUMBER" => new { number = ToDouble(rawValue) },
            "DATE" => new { date = rawValue.GetString() }, // expects "YYYY-MM-DD"
            "SINGLE_SELECT" => new { singleSelectOptionId = ResolveOption(field, rawValue) },
            _ => throw new NotSupportedException(
                $"Field '{field.Name}' has unsupported data type '{field.DataType}' for this example.")
        };

        const string mutation = """
            mutation($projectId: ID!, $itemId: ID!, $fieldId: ID!, $value: ProjectV2FieldValue!) {
              updateProjectV2ItemFieldValue(
                input: { projectId: $projectId, itemId: $itemId, fieldId: $fieldId, value: $value }
              ) {
                projectV2Item { id }
              }
            }
            """;

        await GraphQLAsync(mutation, new { projectId, itemId, fieldId = field.Id, value });
    }

    private static string ResolveOption(ProjectField field, JsonElement rawValue)
    {
        string optionName = rawValue.GetString()
            ?? throw new InvalidOperationException($"Single-select field '{field.Name}' needs a string option name.");
        if (!field.Options.TryGetValue(optionName, out string? optionId))
            throw new InvalidOperationException(
                $"Option '{optionName}' not found on field '{field.Name}'. " +
                $"Available: {string.Join(", ", field.Options.Keys)}");
        return optionId;
    }

    private static double ToDouble(JsonElement value) => value.ValueKind == JsonValueKind.Number
        ? value.GetDouble()
        : double.Parse(value.GetString()!, System.Globalization.CultureInfo.InvariantCulture);

    // ----- transport -----------------------------------------------------------

    /// <summary>Posts a GraphQL query/mutation and returns the "data" element, throwing on GraphQL errors.</summary>
    private async Task<JsonElement> GraphQLAsync(string query, object variables)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.github.com/graphql")
        {
            Content = JsonContent.Create(new { query, variables })
        };
        ApplyHeaders(request);

        using var response = await http.SendAsync(request);
        await EnsureSuccessAsync(response, "execute GraphQL request");

        JsonElement payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (payload.TryGetProperty("errors", out JsonElement errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            throw new InvalidOperationException("GraphQL errors:\n" + errors.GetRawText());

        return payload.GetProperty("data");
    }

    private void ApplyHeaders(HttpRequestMessage request)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", installationToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string what)
    {
        if (response.IsSuccessStatusCode) return;
        string detail = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException(
            $"Failed to {what}: {(int)response.StatusCode} {response.ReasonPhrase}\n{detail}");
    }
}
