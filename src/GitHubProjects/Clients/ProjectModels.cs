using System.Globalization;
using System.Text.Json;

namespace GitHubProjects;

/// <summary>A Projects V2 custom field, with single-select options if applicable.</summary>
public sealed record ProjectField(string Id, string Name, string DataType, IReadOnlyDictionary<string, string> Options);

/// <summary>The result of creating an issue.</summary>
public sealed record CreatedIssue(string NodeId, int Number, string HtmlUrl);

/// <summary>An existing item in a project, with the linked issue/PR number and title when present.</summary>
public sealed record ProjectItem(string Id, int? Number, string? Title);

/// <summary>A single-select field with its full option details (id, name, color, description).</summary>
public sealed record SingleSelectFieldDetail(string FieldId, string Name, IReadOnlyList<SingleSelectOption> Options);

/// <summary>
/// Translates a raw configured/sample value into the GraphQL <c>ProjectV2FieldValue</c> shape
/// for a given field's data type. Pure (no I/O) so it is straightforward to unit test.
/// See https://docs.github.com/en/issues/planning-and-tracking-with-projects/automating-your-project/using-the-api-to-manage-projects
/// </summary>
public static class ProjectFieldValue
{
    /// <summary>Builds the anonymous value object posted as the GraphQL <c>$value</c> variable.</summary>
    public static object Build(ProjectField field, JsonElement rawValue) => field.DataType switch
    {
        "TEXT" => new { text = rawValue.ValueKind == JsonValueKind.String ? rawValue.GetString() : rawValue.ToString() },
        "NUMBER" => new { number = ToDouble(rawValue) },
        "DATE" => new { date = ToDate(rawValue) }, // expects "YYYY-MM-DD"
        "SINGLE_SELECT" => new { singleSelectOptionId = ResolveOption(field, rawValue) },
        _ => throw new NotSupportedException(
            $"Field '{field.Name}' has unsupported data type '{field.DataType}' for this example.")
    };

    public static string ResolveOption(ProjectField field, JsonElement rawValue)
    {
        string optionName = rawValue.ValueKind == JsonValueKind.String
            ? rawValue.GetString()!
            : throw new InvalidOperationException($"Single-select field '{field.Name}' needs a string option name.");

        if (!field.Options.TryGetValue(optionName, out string? optionId))
            throw new InvalidOperationException(
                $"Option '{optionName}' not found on field '{field.Name}'. " +
                $"Available: {string.Join(", ", field.Options.Keys)}");

        return optionId;
    }

    private static double ToDouble(JsonElement value) => value.ValueKind == JsonValueKind.Number
        ? value.GetDouble()
        : double.Parse(value.GetString()!, CultureInfo.InvariantCulture);

    private static string ToDate(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? value.GetString()!
        : throw new InvalidOperationException("Date fields expect a string in 'YYYY-MM-DD' form.");
}
