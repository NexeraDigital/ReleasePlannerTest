using System.ComponentModel.DataAnnotations;

namespace GitHubProjectConnection.Options;

/// <summary>Whether the repo/project is owned by an organization or a user account.</summary>
public enum OwnerType
{
    Organization,
    User
}

/// <summary>
/// Identifies the repository and Projects V2 board to operate on. Bound from the "Target"
/// section of appsettings.json. All values come from the project URL:
/// <c>github.com/orgs/{Owner}/projects/{ProjectNumber}</c>.
/// </summary>
public sealed class TargetOptions
{
    public const string SectionName = "Target";

    /// <summary>Login of the org or user that owns BOTH the repo and the project.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Owner { get; set; } = string.Empty;

    /// <summary>"organization" or "user". Defaults to organization.</summary>
    public OwnerType OwnerType { get; set; } = OwnerType.Organization;

    /// <summary>Repository name (without the owner prefix).</summary>
    [Required(AllowEmptyStrings = false)]
    public string Repo { get; set; } = string.Empty;

    /// <summary>The project's number from its URL.</summary>
    [Range(1, int.MaxValue)]
    public int ProjectNumber { get; set; }

    public bool IsOrganization => OwnerType == OwnerType.Organization;
}
