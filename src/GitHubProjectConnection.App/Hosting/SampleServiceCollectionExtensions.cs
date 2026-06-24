using GitHubProjectConnection.Commands;
using GitHubProjectConnection.Options;
using GitHubProjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GitHubProjectConnection.Hosting;

/// <summary>Registers everything the sample console app needs on top of the GitHubProjects library.</summary>
public static class SampleServiceCollectionExtensions
{
    public static IServiceCollection AddSampleApp(this IServiceCollection services, IConfiguration configuration)
    {
        // App-specific target (owner/repo/project), validated on start.
        services.AddOptions<TargetOptions>()
            .Bind(configuration.GetSection(TargetOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // The reusable GitHub Projects library (App auth + typed clients + resilience).
        services.AddGitHubProjects(configuration.GetSection(GitHubAppOptions.SectionName));

        // Bridge the installation owner from the "Target" section into the library's app options so
        // the token provider can auto-discover the installation id when GitHubApp:InstallationId isn't set.
        string? owner = configuration["Target:Owner"];
        bool ownerIsOrg = !string.Equals(configuration["Target:OwnerType"], "User", StringComparison.OrdinalIgnoreCase);
        services.Configure<GitHubAppOptions>(o =>
        {
            o.Owner ??= owner;
            o.OwnerIsOrganization = ownerIsOrg;
        });

        // Commands + the dispatcher that selects one by CLI flag.
        services.AddSingleton<ISampleCommand, CreateAndPopulateCommand>(); // default (empty flag)
        services.AddSingleton<ISampleCommand, ManageFieldsDemoCommand>();
        services.AddSingleton<ISampleCommand, ConvertDropdownsCommand>();
        services.AddSingleton<ISampleCommand, PopulateExistingCommand>();
        services.AddSingleton<ISampleCommand, ValidateDropdownCommand>();
        services.AddSingleton<ISampleCommand, HelpCommand>();
        services.AddSingleton<SampleCommandDispatcher>();

        return services;
    }
}
