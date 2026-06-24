using System.Globalization;
using System.Net;
using System.Net.Http.Headers;

namespace GitHubProjectConnection.Resilience;

/// <summary>
/// Interprets GitHub's rate-limit signals so the retry pipeline can react correctly:
/// detecting rate-limited responses and computing how long to wait before retrying.
/// Pure (header inspection only) so it can be unit tested with crafted responses.
/// See https://docs.github.com/en/rest/using-the-rest-api/best-practices-for-using-the-rest-api
/// </summary>
public static class GitHubRateLimit
{
    /// <summary>Upper bound on how long we will honor a server-requested delay before giving up.</summary>
    public static readonly TimeSpan MaxHonoredDelay = TimeSpan.FromSeconds(120);

    /// <summary>
    /// True when a response is a rate-limit rejection (vs. a plain permission 403). GitHub signals
    /// this with a 429, or a 403 carrying <c>Retry-After</c> or an exhausted <c>x-ratelimit-remaining</c>.
    /// </summary>
    public static bool IsRateLimited(HttpResponseMessage? response)
    {
        if (response is null) return false;
        if (response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests))
            return false;

        if (response.Headers.RetryAfter is not null) return true;
        return TryGetHeaderLong(response, "x-ratelimit-remaining", out long remaining) && remaining <= 0;
    }

    /// <summary>
    /// The delay GitHub asks us to wait before retrying, or null to fall back to exponential backoff.
    /// Honors <c>Retry-After</c> first, then a primary-limit reset via <c>x-ratelimit-reset</c>.
    /// </summary>
    public static TimeSpan? GetRetryDelay(HttpResponseMessage? response, DateTimeOffset now)
    {
        if (response is null) return null;

        RetryConditionHeaderValue? retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta) return Clamp(delta);
        if (retryAfter?.Date is { } date) return Clamp(date - now);

        if (TryGetHeaderLong(response, "x-ratelimit-remaining", out long remaining) && remaining <= 0 &&
            TryGetHeaderLong(response, "x-ratelimit-reset", out long resetEpoch))
        {
            DateTimeOffset reset = DateTimeOffset.FromUnixTimeSeconds(resetEpoch);
            return Clamp(reset - now);
        }

        return null;
    }

    private static TimeSpan Clamp(TimeSpan delay)
    {
        if (delay < TimeSpan.Zero) return TimeSpan.Zero;
        return delay > MaxHonoredDelay ? MaxHonoredDelay : delay;
    }

    private static bool TryGetHeaderLong(HttpResponseMessage response, string name, out long value)
    {
        value = 0;
        if (!response.Headers.TryGetValues(name, out IEnumerable<string>? values)) return false;
        string? first = values.FirstOrDefault();
        return first is not null &&
               long.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
