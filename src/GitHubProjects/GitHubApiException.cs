using System.Net;
using System.Text.Json;

namespace GitHubProjects;

/// <summary>
/// Raised when a GitHub REST or GraphQL call fails. Carries the HTTP status (for REST)
/// and a best-effort human-readable detail parsed from GitHub's error body.
/// </summary>
public sealed class GitHubApiException : Exception
{
    public HttpStatusCode? StatusCode { get; }

    public GitHubApiException(string message, HttpStatusCode? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }

    /// <summary>
    /// Builds an exception for a failed REST response, pulling GitHub's <c>message</c> field
    /// (and any field-level <c>errors</c>) out of the JSON body when present.
    /// </summary>
    public static GitHubApiException FromRest(string operation, HttpStatusCode status, string? reason, string body)
    {
        string detail = TryParseRestBody(body) ?? Truncate(body);
        return new GitHubApiException(
            $"Failed to {operation}: {(int)status} {reason}. {detail}".TrimEnd(),
            status);
    }

    /// <summary>Builds an exception from a GraphQL <c>errors</c> array.</summary>
    public static GitHubApiException FromGraphQL(string operation, JsonElement errors)
    {
        var messages = new List<string>();
        foreach (JsonElement error in errors.EnumerateArray())
        {
            string? message = error.TryGetProperty("message", out JsonElement m) ? m.GetString() : null;
            string? type = error.TryGetProperty("type", out JsonElement t) ? t.GetString() : null;
            string? path = error.TryGetProperty("path", out JsonElement p) ? p.ToString() : null;

            string line = message ?? "(no message)";
            if (type is not null) line += $" [type: {type}]";
            if (path is not null) line += $" [path: {path}]";
            messages.Add(line);
        }

        string detail = messages.Count > 0 ? string.Join("; ", messages) : "(no error detail)";
        return new GitHubApiException($"GraphQL {operation} failed: {detail}");
    }

    private static string? TryParseRestBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            string? message = root.TryGetProperty("message", out JsonElement m) ? m.GetString() : null;

            var fieldErrors = new List<string>();
            if (root.TryGetProperty("errors", out JsonElement errors) && errors.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement e in errors.EnumerateArray())
                {
                    string? field = e.TryGetProperty("field", out JsonElement f) ? f.GetString() : null;
                    string? code = e.TryGetProperty("code", out JsonElement c) ? c.GetString() : null;
                    if (field is not null || code is not null)
                        fieldErrors.Add($"{field}: {code}".Trim(':', ' '));
                }
            }

            string result = message ?? string.Empty;
            if (fieldErrors.Count > 0) result += $" ({string.Join(", ", fieldErrors)})";
            return string.IsNullOrWhiteSpace(result) ? null : result;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Truncate(string body, int max = 500) =>
        body.Length <= max ? body : body[..max] + "…";
}
