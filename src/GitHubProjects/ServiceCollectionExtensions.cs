using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GitHubProjects;

/// <summary>
/// Registration entry points for the GitHub Projects library. Wires up GitHub App
/// authentication, the typed clients (<see cref="IGitHubIssueClient"/>,
/// <see cref="IGitHubProjectsClient"/>, <see cref="IGitHubFieldManager"/>), and a GitHub-aware
/// HTTP resilience pipeline.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the GitHub Projects clients, binding <see cref="GitHubAppOptions"/> from the given
    /// configuration section and validating it on startup.
    /// </summary>
    public static IServiceCollection AddGitHubProjects(this IServiceCollection services, IConfiguration appConfigSection)
    {
        ArgumentNullException.ThrowIfNull(appConfigSection);

        services.AddOptions<GitHubAppOptions>()
            .Bind(appConfigSection)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return AddCore(services);
    }

    /// <summary>
    /// Adds the GitHub Projects clients, configuring <see cref="GitHubAppOptions"/> inline.
    /// </summary>
    public static IServiceCollection AddGitHubProjects(this IServiceCollection services, Action<GitHubAppOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<GitHubAppOptions>()
            .Configure(configure)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return AddCore(services);
    }

    private static IServiceCollection AddCore(IServiceCollection services)
    {
        // Caches the installation token across calls for the life of the process.
        services.TryAddSingleton<InstallationTokenProvider>();

        // Typed clients: one base address + User-Agent, each wrapped in the GitHub-aware resilience
        // pipeline (retry honoring Retry-After / rate-limit reset, circuit breaker, timeouts).
        services.AddHttpClient<GitHubAppAuthenticator>(ConfigureClient).AddGitHubResilience();
        services.AddHttpClient<IGitHubIssueClient, GitHubRestClient>(ConfigureClient).AddGitHubResilience();
        services.AddHttpClient<IGitHubProjectsClient, GitHubProjectsClient>(ConfigureClient).AddGitHubResilience();
        services.AddHttpClient<IGitHubFieldManager, ManageFieldsClient>(ConfigureClient).AddGitHubResilience();

        return services;
    }

    private static void ConfigureClient(HttpClient client)
    {
        client.BaseAddress = new Uri("https://api.github.com/");
        // GitHub requires a User-Agent header on every request.
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GitHubProjects", "1.0"));
    }
}
