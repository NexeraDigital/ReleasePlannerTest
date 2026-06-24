using System.ComponentModel.DataAnnotations;

namespace GitHubProjectConnection.Options;

/// <summary>
/// Configuration for authenticating as the GitHub App. Bound from the "GitHubApp"
/// section of appsettings.json (or environment variables, e.g. GitHubApp__PrivateKeyPath).
/// </summary>
public sealed class GitHubAppOptions
{
    public const string SectionName = "GitHubApp";

    /// <summary>The App's Client ID (recommended) or numeric App ID — used as the JWT issuer.</summary>
    [Required(AllowEmptyStrings = false)]
    public string ClientIdOrAppId { get; set; } = string.Empty;

    /// <summary>
    /// Path to the App's RS256 private key (.pem). Relative paths are resolved against the
    /// working directory and the binary's output folder. Ignored when <see cref="PrivateKeyPem"/> is set.
    /// </summary>
    public string? PrivateKeyPath { get; set; }

    /// <summary>
    /// The PEM contents directly. Prefer supplying this from a secret store (Key Vault, CI
    /// secret) via the environment variable GitHubApp__PrivateKeyPem instead of a file on disk.
    /// </summary>
    public string? PrivateKeyPem { get; set; }

    /// <summary>Optional installation id. When null/0 it is auto-discovered from <c>Target.Owner</c>.</summary>
    public long? InstallationId { get; set; }
}
