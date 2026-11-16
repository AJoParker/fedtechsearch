using System.Text.RegularExpressions;
using FedTechSearch.Core.Enrichment;
using FedTechSearch.Core.Model;
using FedTechSearch.Core.Taxonomy;

namespace FedTechSearch.Core.Feedback;

/// <summary>What a visitor sends. Every field is untrusted.</summary>
public sealed record FeedbackInput(string? Field, string? CurrentValue, string? SuggestedValue, string? Note);

/// <summary>A report that passed validation, with values trimmed and in canonical spelling.</summary>
public sealed record ValidFeedback(string Field, string? CurrentValue, string? SuggestedValue, string? Note);

public sealed record FeedbackResult(ValidFeedback? Feedback, string? Error)
{
    public bool Ok => Feedback is not null;
    public static FeedbackResult Fail(string error) => new(null, error);
}

/// <summary>
/// Checks a "wrong tag" report against the job and the taxonomy, so reports always point at a
/// real tag and suggest a value the taxonomy knows. Anything else is rejected.
/// </summary>
public static class TagFeedbackValidator
{
    public const int MaxValueLength = 100;
    public const int MaxNoteLength = 500;
    public const int MaxWorkingTitleLength = 80;

    public static readonly IReadOnlyList<string> Fields =
        ["roleFamily", "tech", "workingTitle", "seniority", "clearance", "workArrangement"];

    private static readonly Regex ControlChars = new(@"[\p{Cc}\p{Cf}]", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    public static FeedbackResult Validate(Job job, FeedbackInput input, CompiledTaxonomy taxonomy)
    {
        var field = Fields.FirstOrDefault(f => f.Equals(input.Field?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (field is null) return FeedbackResult.Fail("Unknown field.");

        var current = Clean(input.CurrentValue);
        var suggested = Clean(input.SuggestedValue);
        var note = CleanNote(input.Note);
        if (current?.Length > MaxValueLength || suggested?.Length > MaxValueLength) return FeedbackResult.Fail("Value is too long.");
        if (note?.Length > MaxNoteLength) return FeedbackResult.Fail($"Note is longer than {MaxNoteLength} characters.");

        // The reported value must be one the job actually shows.
        var shown = Shown(job, field);
        if (current is null)
        {
            if (field != "tech") return FeedbackResult.Fail("Pick the value that looks wrong.");
            if (suggested is null) return FeedbackResult.Fail("Name the technology that is missing.");
        }
        else
        {
            var match = shown.FirstOrDefault(v => v.Equals(current, StringComparison.OrdinalIgnoreCase));
            if (match is null) return FeedbackResult.Fail("That value isn't shown on this job.");
            current = match;
        }

        if (suggested is not null)
        {
            var known = Canonical(field, suggested, taxonomy);
            if (known is null) return FeedbackResult.Fail("The suggested value isn't one we know.");
            suggested = known;
            if (string.Equals(suggested, current, StringComparison.Ordinal)) return FeedbackResult.Fail("The suggestion matches the current value.");
            if (field == "tech" && current is null && shown.Contains(suggested)) return FeedbackResult.Fail("That technology is already tagged.");
        }

        return new FeedbackResult(new ValidFeedback(field, current, suggested, note), null);
    }

    /// <summary>The values a job currently shows for a field.</summary>
    public static IReadOnlyList<string> Shown(Job job, string field) => field switch
    {
        "roleFamily" => [job.RoleFamily],
        "tech" => job.Tech,
        "workingTitle" => [job.WorkingTitle],
        "seniority" => job.Seniority,
        "clearance" => [job.Clearance],
        "workArrangement" => [job.WorkArrangement],
        _ => []
    };

    private static string? Canonical(string field, string value, CompiledTaxonomy taxonomy) => field switch
    {
        "roleFamily" => taxonomy.RoleFamilyNames.FirstOrDefault(n => n.Equals(value, StringComparison.OrdinalIgnoreCase)),
        "tech" => taxonomy.CanonicalTechName.TryGetValue(value, out var tech) ? tech : null,
        "seniority" => Seniority.All.FirstOrDefault(s => s.Equals(value, StringComparison.OrdinalIgnoreCase)),
        "clearance" => Clearance.All.FirstOrDefault(c => c.Equals(value, StringComparison.OrdinalIgnoreCase)),
        "workArrangement" => WorkArrangement.All.FirstOrDefault(w => w.Equals(value, StringComparison.OrdinalIgnoreCase)),
        "workingTitle" => value.Length <= MaxWorkingTitleLength ? value : null,
        _ => null
    };

    private static string? Clean(string? value)
    {
        if (value is null) return null;
        var cleaned = Whitespace.Replace(ControlChars.Replace(value, " "), " ").Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }

    /// <summary>Notes keep their line breaks but lose other control characters.</summary>
    private static string? CleanNote(string? value)
    {
        if (value is null) return null;
        var cleaned = Regex.Replace(value.Replace("\r\n", "\n"), @"[\p{Cc}\p{Cf}-[\n]]", "").Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }
}
