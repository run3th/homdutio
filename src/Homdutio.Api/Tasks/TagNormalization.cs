using System.Text.RegularExpressions;

namespace Homdutio.Api.Tasks;

/// <summary>
/// The single place the task-tag rules live (S-12), shared by create, edit, and their tests so the client
/// chip-input and the server can never drift. Normalization is pure and total: trim each tag, collapse
/// internal whitespace runs to a single space, drop blanks, then case-insensitively de-duplicate while
/// preserving the first-seen casing (so "Kitchen" wins over a later "kitchen"). The caps — ≤50 chars per
/// tag and ≤10 tags per task — are rejected (400), never silently truncated, so an over-limit submission is
/// a visible error and a tag is never quietly lost.
/// </summary>
public static partial class TagNormalization
{
    /// <summary>Max characters in a single tag (matches the <c>TaskTag.Value</c> column length).</summary>
    public const int MaxTagLength = 50;

    /// <summary>Max tags on one task.</summary>
    public const int MaxTagsPerTask = 10;

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRuns();

    /// <summary>
    /// Normalizes and validates a raw tag list. On success <see cref="TagNormalizationResult.Errors"/> is
    /// null and <see cref="TagNormalizationResult.Tags"/> holds the cleaned, de-duplicated tags in first-seen
    /// order. On a cap violation <see cref="TagNormalizationResult.Errors"/> carries a <c>Tags</c>-keyed
    /// message in the same shape the endpoints pass to <c>Results.ValidationProblem</c>.
    /// </summary>
    public static TagNormalizationResult Normalize(IEnumerable<string>? input)
    {
        if (input is null)
        {
            return new TagNormalizationResult([], null);
        }

        var cleaned = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in input)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var value = WhitespaceRuns().Replace(raw.Trim(), " ");
            if (value.Length == 0)
            {
                continue;
            }

            if (value.Length > MaxTagLength)
            {
                return Fail($"Each tag must be {MaxTagLength} characters or fewer.");
            }

            // Case-insensitive de-dup, first-seen casing wins.
            if (seen.Add(value))
            {
                cleaned.Add(value);
            }
        }

        if (cleaned.Count > MaxTagsPerTask)
        {
            return Fail($"A task can have at most {MaxTagsPerTask} tags.");
        }

        return new TagNormalizationResult(cleaned, null);
    }

    private static TagNormalizationResult Fail(string message) =>
        new([], new Dictionary<string, string[]> { ["Tags"] = [message] });
}

/// <summary>The outcome of <see cref="TagNormalization.Normalize"/>: cleaned tags, or a validation problem.</summary>
public sealed record TagNormalizationResult(IReadOnlyList<string> Tags, Dictionary<string, string[]>? Errors)
{
    /// <summary>True when normalization succeeded — <see cref="Errors"/> is null and <see cref="Tags"/> is usable.</summary>
    public bool IsValid => Errors is null;
}
