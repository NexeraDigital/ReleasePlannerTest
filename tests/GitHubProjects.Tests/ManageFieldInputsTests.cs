using System.Text.Json;
using GitHubProjects;
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

    [Fact]
    public void MergeNewOptions_keeps_existing_first_then_appends_new()
    {
        var existing = new[]
        {
            new SingleSelectOption("Low", "GRAY", Id: "o1"),
            new SingleSelectOption("High", "RED", Id: "o2"),
        };
        var toAdd = new[] { new SingleSelectOption("At Risk", "RED") };

        var merged = ManageFieldInputs.MergeNewOptions(existing, toAdd);

        Assert.Equal(new[] { "Low", "High", "At Risk" }, merged.Select(o => o.Name));
        Assert.Equal("o1", merged[0].Id);   // existing ids preserved
        Assert.Equal("o2", merged[1].Id);
        Assert.Null(merged[2].Id);          // new option has no id
    }

    [Fact]
    public void MergeNewOptions_skips_names_that_already_exist_case_insensitively()
    {
        var existing = new[] { new SingleSelectOption("High", "RED", Id: "o1") };
        var toAdd = new[] { new SingleSelectOption("high", "GREEN"), new SingleSelectOption("New", "BLUE") };

        var merged = ManageFieldInputs.MergeNewOptions(existing, toAdd);

        Assert.Equal(new[] { "High", "New" }, merged.Select(o => o.Name));
        Assert.Equal("RED", merged[0].Color); // existing 'High' untouched (not replaced by 'high')
    }

    [Fact]
    public void MergeNewOptions_dedupes_within_the_added_batch()
    {
        var existing = Array.Empty<SingleSelectOption>();
        var toAdd = new[] { new SingleSelectOption("A"), new SingleSelectOption("a"), new SingleSelectOption("B") };

        var merged = ManageFieldInputs.MergeNewOptions(existing, toAdd);

        Assert.Equal(new[] { "A", "B" }, merged.Select(o => o.Name));
    }

    [Fact]
    public void MergeNewOptions_returns_existing_count_when_nothing_new()
    {
        var existing = new[] { new SingleSelectOption("High", "RED", Id: "o1") };
        var toAdd = new[] { new SingleSelectOption("HIGH", "GREEN") };

        var merged = ManageFieldInputs.MergeNewOptions(existing, toAdd);

        Assert.Single(merged);
        Assert.Equal(existing.Length, merged.Count); // signals "nothing added" to the caller
    }
}
