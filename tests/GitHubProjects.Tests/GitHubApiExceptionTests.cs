using System.Net;
using System.Text.Json;
using GitHubProjects;
using Xunit;

namespace GitHubProjectConnection.Tests;

public class GitHubApiExceptionTests
{
    [Fact]
    public void FromRest_surfaces_github_message_and_status()
    {
        const string body = """{"message":"Bad credentials","documentation_url":"https://docs.github.com"}""";
        GitHubApiException ex = GitHubApiException.FromRest("create issue", HttpStatusCode.Unauthorized, "Unauthorized", body);

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Contains("create issue", ex.Message);
        Assert.Contains("401", ex.Message);
        Assert.Contains("Bad credentials", ex.Message);
    }

    [Fact]
    public void FromRest_includes_field_level_validation_errors()
    {
        const string body = """
            {"message":"Validation Failed","errors":[{"field":"title","code":"missing_field"}]}
            """;
        GitHubApiException ex = GitHubApiException.FromRest("create issue", HttpStatusCode.UnprocessableEntity, "Unprocessable", body);

        Assert.Contains("Validation Failed", ex.Message);
        Assert.Contains("title", ex.Message);
        Assert.Contains("missing_field", ex.Message);
    }

    [Fact]
    public void FromRest_falls_back_to_raw_body_when_not_json()
    {
        GitHubApiException ex = GitHubApiException.FromRest("do thing", HttpStatusCode.BadGateway, "Bad Gateway", "upstream exploded");
        Assert.Contains("upstream exploded", ex.Message);
    }

    [Fact]
    public void FromGraphQL_concatenates_messages_with_type_and_path()
    {
        using JsonDocument doc = JsonDocument.Parse("""
            [
              {"message":"Could not resolve to a node","type":"NOT_FOUND","path":["node"]}
            ]
            """);
        GitHubApiException ex = GitHubApiException.FromGraphQL("read project fields", doc.RootElement);

        Assert.Contains("read project fields", ex.Message);
        Assert.Contains("Could not resolve to a node", ex.Message);
        Assert.Contains("NOT_FOUND", ex.Message);
    }
}
