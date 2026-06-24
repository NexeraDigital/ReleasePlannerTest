using System.Text.Json;
using GitHubProjectConnection.Clients;
using Xunit;

namespace GitHubProjectConnection.Tests;

public class ManageFieldInputsTests
{
    private static string Serialize(object value) => JsonSerializer.Serialize(value);

    [Fact]
    public void CreateField_text_omits_options()
    {
        var input = ManageFieldInputs.CreateField("PROJ", "TEXT", "Notes", null);
        Assert.Equal("{\"projectId\":\"PROJ\",\"dataType\":\"TEXT\",\"name\":\"Notes\"}", Serialize(input));
    }

    [Fact]
    public void CreateField_single_select_includes_options_with_required_fields()
    {
        var input = ManageFieldInputs.CreateField(
            "PROJ", "SINGLE_SELECT", "Priority",
            new[] { new SingleSelectOption("High", "RED", "Top") });

        Assert.Equal(
            "{\"projectId\":\"PROJ\",\"dataType\":\"SINGLE_SELECT\",\"name\":\"Priority\"," +
            "\"singleSelectOptions\":[{\"name\":\"High\",\"color\":\"RED\",\"description\":\"Top\"}]}",
            Serialize(input));
    }

    [Fact]
    public void CreateField_single_select_without_options_throws()
    {
        Assert.Throws<ArgumentException>(
            () => ManageFieldInputs.CreateField("PROJ", "SINGLE_SELECT", "Priority", null));
    }

    [Fact]
    public void UpdateField_only_includes_provided_parts()
    {
        // Only fieldId + name; no options key when options is null.
        var input = ManageFieldInputs.UpdateField("FIELD", "Renamed", null);
        Assert.Equal("{\"fieldId\":\"FIELD\",\"name\":\"Renamed\"}", Serialize(input));
    }

    [Fact]
    public void UpdateField_existing_option_keeps_id_new_option_omits_it()
    {
        var input = ManageFieldInputs.UpdateField(
            "FIELD", null,
            new[]
            {
                new SingleSelectOption("Keep", "GREEN", Id: "opt-1"), // existing -> id preserved
                new SingleSelectOption("New", "BLUE"),                 // new -> no id
            });

        Assert.Equal(
            "{\"fieldId\":\"FIELD\",\"singleSelectOptions\":[" +
            "{\"name\":\"Keep\",\"color\":\"GREEN\",\"description\":\"\",\"id\":\"opt-1\"}," +
            "{\"name\":\"New\",\"color\":\"BLUE\",\"description\":\"\"}]}",
            Serialize(input));
    }

    [Fact]
    public void DeleteField_carries_field_id()
    {
        Assert.Equal("{\"fieldId\":\"FIELD\"}", Serialize(ManageFieldInputs.DeleteField("FIELD")));
    }
}
