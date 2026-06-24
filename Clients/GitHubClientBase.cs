using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GitHubProjectConnection.Auth;
using GitHubProjectConnection.Errors;

namespace GitHubProjectConnection.Clients;

/// <summary>
/// Shared plumbing for the REST and GraphQL clients: attaches the installation token and the
/// required GitHub headers to each request, and turns failed responses into rich exceptions.
/// </summary>
public abstract class GitHubClientBase
{
    protected HttpClient Http { get; }

    private readonly InstallationTokenProvider _tokenProvider;

    protected GitHubClientBase(HttpClient http, InstallationTokenProvider tokenProvider)
    {
        Http = http;
        _tokenProvider = tokenProvider;
    }

    protected async Task ApplyHeadersAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string token = await _tokenProvider.GetTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    protected static async Task EnsureSuccessAsync(
        HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw GitHubApiException.FromRest(operation, response.StatusCode, response.ReasonPhrase, body);
    }

    /// <summary>
    /// Posts a GraphQL query/mutation to <c>/graphql</c> and returns the <c>data</c> element,
    /// throwing a <see cref="GitHubApiException"/> if the response carries GraphQL errors.
    /// Shared by every GraphQL-backed client (Projects V2 is GraphQL-only).
    /// </summary>
    protected async Task<JsonElement> GraphQLAsync(
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
