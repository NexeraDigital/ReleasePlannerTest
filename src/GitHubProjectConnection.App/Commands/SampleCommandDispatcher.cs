namespace GitHubProjectConnection.Commands;

/// <summary>
/// Selects and runs an <see cref="ISampleCommand"/> based on the command-line flags. The first
/// command whose <see cref="ISampleCommand.Flag"/> appears in the args wins; otherwise the
/// default command (the one with an empty flag) runs.
/// </summary>
public sealed class SampleCommandDispatcher
{
    private readonly IReadOnlyList<ISampleCommand> _commands;

    public SampleCommandDispatcher(IEnumerable<ISampleCommand> commands)
    {
        _commands = commands.ToList();
    }

    /// <summary>All registered commands, for help output.</summary>
    public IReadOnlyList<ISampleCommand> Commands => _commands;

    public Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        ISampleCommand command =
            _commands.FirstOrDefault(c => c.Flag.Length > 0 && args.Contains(c.Flag, StringComparer.OrdinalIgnoreCase))
            ?? _commands.First(c => c.Flag.Length == 0);

        return command.RunAsync(cancellationToken);
    }
}
