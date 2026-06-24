namespace GitHubProjectConnection.Commands;

/// <summary>
/// A runnable sample command, selected by its <see cref="Flag"/> on the command line.
/// The command with an empty <see cref="Flag"/> is the default (run when no flag is given).
/// Implementations receive their dependencies via constructor injection.
/// </summary>
public interface ISampleCommand
{
    /// <summary>The CLI flag that selects this command (e.g. <c>--validate-dropdown</c>), or "" for the default.</summary>
    string Flag { get; }

    /// <summary>One-line description shown by <c>--help</c>.</summary>
    string Description { get; }

    /// <summary>Runs the command and returns a process exit code.</summary>
    Task<int> RunAsync(CancellationToken cancellationToken);
}
