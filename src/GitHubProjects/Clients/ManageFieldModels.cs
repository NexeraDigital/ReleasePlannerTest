namespace GitHubProjects;

/// <summary>
/// One option of a single-select field. Supply <see cref="Id"/> to update/keep an existing
/// option (preserving values already set to it); omit it to create a new option. Any existing
/// option NOT included when updating a field is removed.
/// <para>
/// <see cref="Color"/> is a GitHub enum: GRAY, BLUE, GREEN, YELLOW, ORANGE, RED, PINK, PURPLE.
/// </para>
/// </summary>
public sealed record SingleSelectOption(string Name, string Color = "GRAY", string? Description = null, string? Id = null);

/// <summary>
/// Builds the GraphQL input objects for the field-definition mutations
/// (create/update/delete <c>ProjectV2Field</c>). Pure (no I/O) so it is easy to unit test.
/// Enum-typed members (<c>dataType</c>, option <c>color</c>) are sent as strings; GitHub coerces
/// them to the enum because they ride inside a typed input variable.
/// </summary>
public static class ManageFieldInputs
{
    /// <summary>Input for <c>createProjectV2Field</c>. Single-select fields require ≥1 option.</summary>
    public static Dictionary<string, object?> CreateField(
        string projectId, string dataType, string name, IReadOnlyList<SingleSelectOption>? options)
    {
        var input = new Dictionary<string, object?>
        {
            ["projectId"] = projectId,
            ["dataType"] = dataType,
            ["name"] = name,
        };

        if (string.Equals(dataType, "SINGLE_SELECT", StringComparison.OrdinalIgnoreCase))
        {
            if (options is null || options.Count == 0)
                throw new ArgumentException("SINGLE_SELECT fields require at least one option.", nameof(options));
            input["singleSelectOptions"] = options.Select(ToOption).ToArray();
        }

        return input;
    }

    /// <summary>
    /// Input for <c>updateProjectV2Field</c>. Only the provided parts change: pass <paramref name="name"/>
    /// to rename; pass <paramref name="options"/> to set the FULL option list (include each existing
    /// option's <c>Id</c> to keep it). Omitted arguments are left untouched.
    /// </summary>
    public static Dictionary<string, object?> UpdateField(
        string fieldId, string? name, IReadOnlyList<SingleSelectOption>? options)
    {
        var input = new Dictionary<string, object?> { ["fieldId"] = fieldId };
        if (name is not null) input["name"] = name;
        if (options is not null) input["singleSelectOptions"] = options.Select(ToOption).ToArray();
        return input;
    }

    /// <summary>Input for <c>deleteProjectV2Field</c>.</summary>
    public static Dictionary<string, object?> DeleteField(string fieldId) =>
        new() { ["fieldId"] = fieldId };

    /// <summary>
    /// Produces the full single-select option list for an additive update: the existing options
    /// (kept first, in order) followed by any of <paramref name="toAdd"/> whose name isn't already
    /// present (case-insensitive). Existing options are returned unchanged so their ids — and the
    /// item values assigned to them — are preserved when sent to <c>updateProjectV2Field</c>.
    /// </summary>
    public static IReadOnlyList<SingleSelectOption> MergeNewOptions(
        IReadOnlyList<SingleSelectOption> existing, IReadOnlyList<SingleSelectOption> toAdd)
    {
        var names = new HashSet<string>(existing.Select(o => o.Name), StringComparer.OrdinalIgnoreCase);
        return existing.Concat(toAdd.Where(o => names.Add(o.Name))).ToList();
    }

    private static Dictionary<string, object?> ToOption(SingleSelectOption o)
    {
        // name, color, and description are all required by ProjectV2SingleSelectFieldOptionInput.
        var option = new Dictionary<string, object?>
        {
            ["name"] = o.Name,
            ["color"] = o.Color,
            ["description"] = o.Description ?? string.Empty,
        };
        if (o.Id is not null) option["id"] = o.Id; // present => update/keep; absent => new option
        return option;
    }
}
