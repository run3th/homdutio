using Homdutio.Api.Tasks;

namespace Homdutio.Api.Tests;

/// <summary>
/// Pure-function coverage for the S-12 tag rules shared by create + edit (and mirrored client-side): trim,
/// collapse internal whitespace, drop blanks, case-insensitive de-dup preserving first-seen casing, and the
/// ≤50-chars / ≤10-tags caps that reject (never truncate). No host/DB — these assert the rules directly.
/// </summary>
public class TagNormalizationTests
{
    [Fact]
    public void Trims_and_collapses_internal_whitespace()
    {
        var result = TagNormalization.Normalize(["  multi   word  tag "]);
        Assert.True(result.IsValid);
        Assert.Equal(["multi word tag"], result.Tags);
    }

    [Fact]
    public void Drops_blank_and_whitespace_only_entries()
    {
        var result = TagNormalization.Normalize(["", "   ", "kitchen", "\t"]);
        Assert.True(result.IsValid);
        Assert.Equal(["kitchen"], result.Tags);
    }

    [Fact]
    public void De_dups_case_insensitively_preserving_first_seen_casing()
    {
        var result = TagNormalization.Normalize(["Kitchen", "kitchen", "KITCHEN", "Garden"]);
        Assert.True(result.IsValid);
        Assert.Equal(["Kitchen", "Garden"], result.Tags);
    }

    [Fact]
    public void Null_input_yields_empty_set()
    {
        var result = TagNormalization.Normalize(null);
        Assert.True(result.IsValid);
        Assert.Empty(result.Tags);
    }

    [Fact]
    public void A_tag_over_the_length_cap_is_rejected()
    {
        var result = TagNormalization.Normalize([new string('x', TagNormalization.MaxTagLength + 1)]);
        Assert.False(result.IsValid);
        Assert.True(result.Errors!.ContainsKey("Tags"));
    }

    [Fact]
    public void A_tag_at_exactly_the_length_cap_is_accepted()
    {
        var exact = new string('x', TagNormalization.MaxTagLength);
        var result = TagNormalization.Normalize([exact]);
        Assert.True(result.IsValid);
        Assert.Equal([exact], result.Tags);
    }

    [Fact]
    public void More_than_the_max_distinct_tags_is_rejected()
    {
        var many = Enumerable.Range(0, TagNormalization.MaxTagsPerTask + 1).Select(i => $"tag{i}").ToArray();
        var result = TagNormalization.Normalize(many);
        Assert.False(result.IsValid);
        Assert.True(result.Errors!.ContainsKey("Tags"));
    }

    [Fact]
    public void Exactly_the_max_distinct_tags_is_accepted()
    {
        var many = Enumerable.Range(0, TagNormalization.MaxTagsPerTask).Select(i => $"tag{i}").ToArray();
        var result = TagNormalization.Normalize(many);
        Assert.True(result.IsValid);
        Assert.Equal(TagNormalization.MaxTagsPerTask, result.Tags.Count);
    }
}
