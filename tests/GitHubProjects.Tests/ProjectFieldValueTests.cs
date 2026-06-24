using System.Text.Json;
using GitHubProjects;
using Xunit;

namespace GitHubProjectConnection.Tests;

public class ProjectFieldValueTests
{
    private static ProjectField Field(string dataType, params (string name, string id)[] options)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, string id) in options) dict[name] = id;
        return new ProjectField("FIELD_ID", "Field", dataType, dict);
    }

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    private static string Serialize(object value) => JsonSerializer.Serialize(value);

    [Fact]
    public void Text_field_wraps_string_value()
    {
        object result = ProjectFieldValue.Build(Field("TEXT"), Json("hello"));
        Assert.Equal("{\"text\":\"hello\"}", Serialize(result));
    }

    [Fact]
    public void Number_field_accepts_json_number()
    {
        object result = ProjectFieldValue.Build(Field("NUMBER"), Json(3));
        Assert.Equal("{\"number\":3}", Serialize(result));
    }

    [Fact]
    public void Number_field_parses_numeric_string()
    {
        object result = ProjectFieldValue.Build(Field("NUMBER"), Json("2.5"));
        Assert.Equal("{\"number\":2.5}", Serialize(result));
    }

    [Fact]
    public void Date_field_passes_through_iso_string()
    {
        object result = ProjectFieldValue.Build(Field("DATE"), Json("2026-07-01"));
        Assert.Equal("{\"date\":\"2026-07-01\"}", Serialize(result));
    }

    [Fact]
    public void Single_select_resolves_option_name_to_id()
    {
        object result = ProjectFieldValue.Build(
            Field("SINGLE_SELECT", ("In Progress", "opt-123")), Json("In Progress"));
        Assert.Equal("{\"singleSelectOptionId\":\"opt-123\"}", Serialize(result));
    }

    [Fact]
    public void Single_select_is_case_insensitive()
    {
        object result = ProjectFieldValue.Build(
            Field("SINGLE_SELECT", ("In Progress", "opt-123")), Json("in progress"));
        Assert.Equal("{\"singleSelectOptionId\":\"opt-123\"}", Serialize(result));
    }

    [Fact]
    public void Single_select_unknown_option_throws_with_available_options()
    {
        ProjectField field = Field("SINGLE_SELECT", ("Todo", "o1"), ("Done", "o2"));
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => ProjectFieldValue.Build(field, Json("Nope")));
        Assert.Contains("Todo", ex.Message);
        Assert.Contains("Done", ex.Message);
    }

    [Fact]
    public void Unsupported_data_type_throws_not_supported()
    {
        Assert.Throws<NotSupportedException>(
            () => ProjectFieldValue.Build(Field("ITERATION"), Json("x")));
    }
}
