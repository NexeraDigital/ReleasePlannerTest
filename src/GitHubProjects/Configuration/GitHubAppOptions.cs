using System.ComponentModel.DataAnnotations;

namespace GitHubProjects;

/// <summary>
/// Configuration for authenticating as a GitHub App. Typically bound from a "GitHubApp"
/// configuration section (or environment variables, e.g. GitHubApp__PrivateKeyPath).
/// </summary>
public sealed class GitHubAppOptions
{
    /// <summary>Conventional configuration section name.</summary>
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

    /// <summary>Optional installation id. When null/0 it is auto-discovered from <see cref="Owner"/>.</summary>
    public long? InstallationId { get; set; }

    /// <summary>
    /// The org/user login the App is installed on, used to auto-discover the installation id when
    /// <see cref="InstallationId"/> is not set. Not required when an installation id is supplied.
    /// </summary>
    public string? Owner { get; set; }

    /// <summary>Whether <see cref="Owner"/> is an organization (true) or a user account (false).</summary>
    public bool OwnerIsOrganization { get; set; } = true;
}
