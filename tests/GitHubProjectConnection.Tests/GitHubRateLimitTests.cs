using System.Net;
using System.Net.Http.Headers;
using GitHubProjectConnection.Resilience;
using Xunit;

namespace GitHubProjectConnection.Tests;

public class GitHubRateLimitTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddYears(56); // fixed reference

    private static HttpResponseMessage Response(HttpStatusCode status, Action<HttpResponseMessage>? configure = null)
    {
        var response = new HttpResponseMessage(status);
        configure?.Invoke(response);
        return response;
    }

    [Fact]
    public void Plain_403_is_not_treated_as_rate_limited()
    {
        using HttpResponseMessage response = Response(HttpStatusCode.Forbidden);
        Assert.False(GitHubRateLimit.IsRateLimited(response));
    }

    [Fact]
    public void Forbidden_with_retry_after_is_rate_limited()
    {
        using HttpResponseMessage response = Response(HttpStatusCode.Forbidden, r =>
            r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30)));
        Assert.True(GitHubRateLimit.IsRateLimited(response));
    }

    [Fact]
    public void Forbidden_with_exhausted_remaining_is_rate_limited()
    {
        using HttpResponseMessage response = Response(HttpStatusCode.Forbidden, r =>
            r.Headers.Add("x-ratelimit-remaining", "0"));
        Assert.True(GitHubRateLimit.IsRateLimited(response));
    }

    [Fact]
    public void Success_is_not_rate_limited()
    {
        using HttpResponseMessage response = Response(HttpStatusCode.OK);
        Assert.False(GitHubRateLimit.IsRateLimited(response));
    }

    [Fact]
    public void Retry_after_delta_is_honored()
    {
        using HttpResponseMessage response = Response(HttpStatusCode.Forbidden, r =>
            r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30)));

        TimeSpan? delay = GitHubRateLimit.GetRetryDelay(response, Now);
        Assert.Equal(TimeSpan.FromSeconds(30), delay);
    }

    [Fact]
    public void Retry_after_is_clamped_to_the_maximum()
    {
        using HttpResponseMessage response = Response(HttpStatusCode.Forbidden, r =>
            r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(600)));

        TimeSpan? delay = GitHubRateLimit.GetRetryDelay(response, Now);
        Assert.Equal(GitHubRateLimit.MaxHonoredDelay, delay);
    }

    [Fact]
    public void Primary_limit_reset_computes_delay_until_reset()
    {
        long resetEpoch = Now.AddSeconds(45).ToUnixTimeSeconds();
        using HttpResponseMessage response = Response(HttpStatusCode.Forbidden, r =>
        {
            r.Headers.Add("x-ratelimit-remaining", "0");
            r.Headers.Add("x-ratelimit-reset", resetEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture));
        });

        TimeSpan? delay = GitHubRateLimit.GetRetryDelay(response, Now);
        Assert.Equal(TimeSpan.FromSeconds(45), delay);
    }

    [Fact]
    public void No_rate_limit_headers_returns_null_for_backoff_fallback()
    {
        using HttpResponseMessage response = Response(HttpStatusCode.InternalServerError);
        Assert.Null(GitHubRateLimit.GetRetryDelay(response, Now));
    }
}
