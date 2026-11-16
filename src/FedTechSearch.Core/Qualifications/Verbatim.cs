using System.Text.RegularExpressions;

namespace FedTechSearch.Core.Qualifications;

/// <summary>
/// The no-paraphrase gate. Qualifications decide whether HR screens someone out, so every
/// item shown must be a span of the source text: compared case-insensitively with whitespace
/// collapsed, and nothing else forgiven.
/// </summary>
public static class Verbatim
{
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    public static string Normalize(string text) => Spaces.Replace(text, " ").Trim().ToLowerInvariant();

    /// <summary>True when <paramref name="item"/> appears in the already-normalized source.</summary>
    public static bool Contains(string normalizedSource, string item)
    {
        var needle = Normalize(item);
        return needle.Length > 0 && normalizedSource.Contains(needle, StringComparison.Ordinal);
    }

    /// <summary>
    /// The only edits allowed on an extracted span: leading/trailing whitespace, trailing
    /// punctuation and connectives ("…, and"), and capitalizing the first letter. No words are added.
    /// </summary>
    public static string Tidy(string span)
    {
        var s = Spaces.Replace(span, " ").Trim();
        s = Regex.Replace(s, @"^(?:examples? of (?:qualifying |specialized )?experience[^:]{0,40}:\s*)", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"^(?:and|or|which includes|that includes|includes|such as)\s+", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"(?:[\s,;:.]+|\s+(?:and|or))+$", "", RegexOptions.IgnoreCase);
        return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    }
}
