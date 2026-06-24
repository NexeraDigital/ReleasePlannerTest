using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace GitHubProjects;

/// <summary>
/// Wires the .NET "standard resilience handler" (retry → circuit breaker → timeout) onto a
/// typed client, then teaches its retry stage to obey GitHub's rate-limit signals.
///
/// The stock handler retries generic transient faults (5xx/408/429) with blind exponential
/// backoff. GitHub additionally asks callers to respect <c>Retry-After</c> and, when
/// <c>x-ratelimit-remaining</c> is 0, to wait until <c>x-ratelimit-reset</c>. That GitHub-specific
/// interpretation lives in <see cref="GitHubRateLimit"/>.
/// See https://docs.github.com/en/rest/using-the-rest-api/best-practices-for-using-the-rest-api
/// </summary>
internal static class GitHubResilience
{
    public static IHttpClientBuilder AddGitHubResilience(this IHttpClientBuilder builder)
    {
        builder.AddStandardResilienceHandler(options =>
        {
            // A single rate-limit wait can exceed the default 30s overall budget.
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(150);
            options.Retry.MaxRetryAttempts = 4;
            options.Retry.UseJitter = true;

            // Retry the usual transient faults, plus GitHub's rate-limit 403/429 responses.
            options.Retry.ShouldHandle = args => new ValueTask<bool>(
                HttpClientResiliencePredicates.IsTransient(args.Outcome) ||
                GitHubRateLimit.IsRateLimited(args.Outcome.Result));

            // When GitHub tells us when it is safe to retry, honor that instead of blind backoff.
            // Returning null falls back to the strategy's exponential backoff.
            options.Retry.DelayGenerator = args => new ValueTask<TimeSpan?>(
                GitHubRateLimit.GetRetryDelay(args.Outcome.Result, DateTimeOffset.UtcNow));
        });

        return builder;
    }
}
