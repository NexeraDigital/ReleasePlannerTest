using System.Net.Http.Json;
using System.Text.Json;
using GitHubProjectConnection.Auth;

namespace GitHubProjectConnection.Clients;

/// <summary>Talks to GitHub's REST API as the authenticated installation (issue creation).</summary>
public sealed class GitHubRestClient : GitHubClientBase
{
    public GitHubRestClient(HttpClient http, InstallationTokenProvider tokenProvider)
        : base(http, tokenProvider)
    {
    }

    /// <summary>POST /repos/{owner}/{repo}/issues — returns the issue's node id and number.</summary>
    public async Task<CreatedIssue> CreateIssueAsync(
        string owner, string repo, string title, string? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"repos/{owner}/{repo}/issues")
        {
            Content = JsonContent.Create(new { title, body })
        };
        await ApplyHeadersAsync(request, cancellationToken);

        using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "create issue", cancellationToken);

        JsonElement issue = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return new CreatedIssue(
            issue.GetProperty("node_id").GetString()!,
            issue.GetProperty("number").GetInt32(),
            issue.GetProperty("html_url").GetString()!);
    }
}
