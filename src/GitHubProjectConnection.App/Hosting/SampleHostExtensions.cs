using GitHubProjectConnection.Commands;
using GitHubProjectConnection.Options;
using GitHubProjects;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GitHubProjectConnection.Hosting;

/// <summary>Runs the selected sample command with Ctrl+C cancellation and exit-code mapping.</summary>
public static class SampleHostExtensions
{
    public static async Task<int> RunSampleAsync(this IHost host, string[] args)
    {
        ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Program");

        // Force options validation now so misconfiguration fails with a clear message up front.
        _ = host.Services.GetRequiredService<IOptions<TargetOptions>>().Value;
        _ = host.Services.GetRequiredService<IOptions<GitHubAppOptions>>().Value;

        // Ctrl+C requests a graceful cancellation that flows through every API call.
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        SampleCommandDispatcher dispatcher = host.Services.GetRequiredService<SampleCommandDispatcher>();

        try
        {
            return await dispatcher.RunAsync(args, cts.Token);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Cancelled.");
            return 130;
        }
        catch (GitHubApiException ex)
        {
            logger.LogError("{Message}", ex.Message);
            return 1;
        }
    }
}
