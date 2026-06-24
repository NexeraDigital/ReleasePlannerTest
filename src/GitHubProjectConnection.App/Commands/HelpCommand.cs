using Microsoft.Extensions.DependencyInjection;

namespace GitHubProjectConnection.Commands;

/// <summary>Prints the available commands and their descriptions.</summary>
public sealed class HelpCommand : ISampleCommand
{
    private readonly IServiceProvider _services;

    // Resolve the command list lazily (in RunAsync) rather than via the constructor, so this
    // command can list the others without creating a circular dependency on IEnumerable<ISampleCommand>.
    public HelpCommand(IServiceProvider services) => _services = services;

    public string Flag => "--help";
    public string Description => "Show this help and exit.";

    public Task<int> RunAsync(CancellationToken cancellationToken)
    {
        IEnumerable<ISampleCommand> commands = _services.GetServices<ISampleCommand>()
            .OrderBy(c => c.Flag.Length == 0 ? 0 : 1)             // default first
            .ThenBy(c => c.Flag, StringComparer.Ordinal);

        Console.WriteLine("GitHub Projects sample — usage:");
        Console.WriteLine("  dotnet run --project src/GitHubProjectConnection.App [-- <command>]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        foreach (ISampleCommand command in commands)
        {
            string flag = command.Flag.Length == 0 ? "(default)" : command.Flag;
            Console.WriteLine($"  {flag,-22} {command.Description}");
        }

        return Task.FromResult(0);
    }
}
