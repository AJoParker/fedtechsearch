using System.Text.RegularExpressions;
using P = FedTechSearch.Core.Qualifications.QualificationPatterns;

namespace FedTechSearch.Core.Qualifications;

/// <summary>A readable view of a posting's qualifications. Every string is a verbatim span of the source.</summary>
public sealed class QualificationInfo
{
    /// <summary>False when the posting doesn't follow a recognizable template; the UI then shows the original text.</summary>
    public bool Parsed { get; set; }
    public string? WhoMayApply { get; set; }
    public List<QualificationBlock> Blocks { get; set; } = [];
    public List<string> StandardCompetencies { get; set; } = [];
    public List<string> Notes { get; set; } = [];

    /// <summary>SES/ST postings: numbered Technical Qualifications, verbatim.</summary>
    public List<TechnicalQualification> TechnicalQualifications { get; set; } = [];

    /// <summary>SES postings: the Executive Core Qualifications named in the text.</summary>
    public List<string> ExecutiveCoreQualifications { get; set; } = [];
}

public sealed class TechnicalQualification
{
    /// <summary>"TQ 1".</summary>
    public string Label { get; set; } = "";

    /// <summary>The heading before a colon, when the posting gives one.</summary>
    public string? Title { get; set; }
    public string Text { get; set; } = "";
}

public sealed class QualificationBlock
{
    /// <summary>e.g. "GS-11". From the text, or the posting's own grade when the text names none.</summary>
    public string? Grade { get; set; }
    public string? EquivalentGrade { get; set; }

    /// <summary>"One year of specialized experience" when the source says so.</summary>
    public string? ExperienceLabel { get; set; }
    public List<string> ExperienceDuties { get; set; } = [];
    public EducationPath? Education { get; set; }
}

public sealed class EducationPath
{
    public string Requirement { get; set; } = "";
    public List<string> Fields { get; set; } = [];

    /// <summary>Other ways to qualify by education ("…; or, three full years of graduate education…").</summary>
    public List<string> Alternatives { get; set; } = [];
}

public sealed record ParseResult(QualificationInfo Info, IReadOnlyList<string> Rejected);

/// <summary>
/// Turns USAJOBS qualifications text into <see cref="QualificationInfo"/>. Patterns live in
/// <see cref="QualificationPatterns"/>. Items that fail the verbatim check are dropped and returned
/// in <see cref="ParseResult.Rejected"/>; the parser never writes its own wording.
/// </summary>
public static class QualificationParser
{
    private const int MaxDutySentences = 8;

    public static ParseResult Parse(string? qualifications, string? education, string? whoMayApply, string? payPlan, int? lowGrade, int? highGrade)
    {
        var text = Regex.Replace(qualifications ?? "", @"\s+", " ").Trim();
        var info = new QualificationInfo();
        var rejected = new List<string>();
        if (text.Length == 0) return new ParseResult(info, rejected);

        // Every item is checked against the text it was taken from.
        var source = Verbatim.Normalize(text);
        string? KeepIn(string? span, string normalizedSource)
        {
            if (span is null) return null;
            var tidy = Verbatim.Tidy(span);
            if (tidy.Length == 0) return null;
            if (Verbatim.Contains(normalizedSource, tidy)) return tidy;
            rejected.Add(tidy);
            return null;
        }
        string? Keep(string? span) => KeepIn(span, source);

        // Competencies: detected, listed as chips, and masked so duties never come from boilerplate.
        var (competencies, masked) = FindCompetencies(text);
        info.StandardCompetencies = competencies;

        // Only a value that names an audience becomes the badge; otherwise there's no badge, not a guess.
        var who = P.WhoMayApply.Match(text);
        info.WhoMayApply = who.Success
            ? (P.Audience.IsMatch(who.Groups["value"].Value) ? Keep(who.Groups["value"].Value) : null)
            : string.IsNullOrWhiteSpace(whoMayApply) || !P.Audience.IsMatch(whoMayApply) ? null : whoMayApply.Trim();

        info.Notes = FindNotes(text).Select(Keep).OfType<string>().Distinct().ToList();

        var postingGrade = PostingGrade(payPlan, lowGrade, highGrade);
        var headings = P.GradeHeading.Matches(masked).Cast<Match>()
            .GroupBy(m => m.Groups["grade"].Value).Select(g => g.First()).OrderBy(m => m.Index).ToList();

        // One block per grade heading; without headings, the whole text is one block.
        var spans = new List<(string? Grade, int Start, int End)>();
        if (headings.Count == 0)
        {
            var titled = P.SeriesGrade.Match(text);
            spans.Add((titled.Success ? GradeOf(titled) : postingGrade, 0, masked.Length));
        }
        else
        {
            for (var i = 0; i < headings.Count; i++)
                spans.Add((GradeOf(headings[i]), headings[i].Index, i + 1 < headings.Count ? headings[i + 1].Index : masked.Length));
        }

        foreach (var (grade, start, end) in spans)
        {
            var body = masked[start..end];
            var block = new QualificationBlock { Grade = grade };
            if (P.EquivalentGrade.Match(body) is { Success: true } eq) block.EquivalentGrade = GradeOf(eq);
            if (P.ExperienceLabel.Match(body) is { Success: true } label) block.ExperienceLabel = Keep(label.Value);

            block.ExperienceDuties = Duties(body).Select(Keep).OfType<string>().Distinct().ToList();
            if (block.ExperienceDuties.Count > 0) info.Blocks.Add(block);
        }

        info.TechnicalQualifications = FindTechnicalQualifications(text, Keep);
        info.ExecutiveCoreQualifications = Fingerprints(text, P.ExecutiveCore).Select(f => f.Name).ToList();

        // The education path applies to every block (OPM 2210 postings state it once).
        var path = FindEducation(text, education, KeepIn);
        if (path is not null) foreach (var b in info.Blocks) b.Education = path;

        info.Parsed = info.Blocks.Count > 0 || info.TechnicalQualifications.Count > 0;
        return new ParseResult(info, rejected);
    }

    /// <summary>"GS-11" from a match with "plan" and "grade" groups; a leading zero is kept ("GS-09").</summary>
    private static string GradeOf(Match m) => $"{m.Groups["plan"].Value.ToUpperInvariant()}-{m.Groups["grade"].Value}";

    private static string? PostingGrade(string? payPlan, int? low, int? high)
    {
        if (string.IsNullOrWhiteSpace(payPlan) || low is null) return null;
        return high is { } h && h != low ? $"{payPlan}-{low}/{h}" : $"{payPlan}-{low}";
    }

    /// <summary>Which fingerprints appear: by heading, or by two distinctive phrases (one if that's all it has).</summary>
    private static List<(string Name, int Index)> Fingerprints(string text, CompetencyFingerprint[] fingerprints)
    {
        var lower = text.ToLowerInvariant();
        var found = new List<(string, int)>();
        foreach (var c in fingerprints)
        {
            var heading = c.Heading.Match(text);
            var hits = c.Phrases.Where(p => lower.Contains(p, StringComparison.Ordinal)).ToList();
            if (!heading.Success && hits.Count < Math.Min(2, c.Phrases.Length)) continue;
            found.Add((c.Name, heading.Success ? heading.Index : lower.IndexOf(hits[0], StringComparison.Ordinal)));
        }
        return found;
    }

    /// <summary>Finds which standard competencies appear, and returns the text with their definitions blanked out.</summary>
    private static (List<string> Names, string Masked) FindCompetencies(string text)
    {
        var hits = Fingerprints(text, P.Competencies);
        var found = hits.Select(h => h.Name).ToList();
        var starts = hits.Select(h => h.Index).ToList();
        if (found.Count == 0) return (found, text);

        // Mask from the first competency to the next section marker after the last one.
        var from = starts.Min();
        var last = starts.Max();
        var stop = P.DutyStop.Match(text, last);
        var grade = P.GradeHeading.Match(text, last);
        var experience = P.ExperienceLabel.Match(text, last);
        var ends = new[] { stop, grade, experience }.Where(m => m.Success).Select(m => m.Index).DefaultIfEmpty(text.Length);
        var to = Math.Min(ends.Min(), last + 2500);
        var masked = text[..from] + new string(' ', to - from) + text[to..];
        return (found, masked);
    }

    /// <summary>The duty list after the first intro, split on numbering, sentences (after a colon), semicolons and safe gerund lists.</summary>
    private static IEnumerable<string> Duties(string body)
    {
        var intro = P.DutyIntro.Match(body);
        if (!intro.Success) yield break;

        var rest = body[(intro.Index + intro.Length)..];
        var stop = P.DutyStop.Match(rest);
        var nextHeading = P.GradeHeading.Match(rest);
        var cut = new[] { stop, nextHeading }.Where(m => m.Success).Select(m => m.Index).DefaultIfEmpty(rest.Length).Min();
        rest = rest[..cut].Trim();
        if (rest.Length == 0) yield break;

        IEnumerable<string> pieces;
        var numbered = P.NumberedListStart.IsMatch(rest);
        if (numbered)
        {
            // The list runs to the end of the sentence holding its last number, so an item that
            // ends with a period ("…acquisitions. 3. Acting…") doesn't cut the list short.
            var last = P.NumberMarker.Matches(rest).Cast<Match>().Last();
            var list = rest[..(last.Index + last.Length + SentenceEnd(rest[(last.Index + last.Length)..]))];
            pieces = P.NumberMarker.Split(list).Where(p => p.Trim().Length > 0);
        }
        else
        {
            var endsWithColon = intro.Value.TrimEnd().EndsWith(':');
            var sentences = Sentences(rest).ToList();
            pieces = endsWithColon ? sentences.Take(MaxDutySentences) : sentences.Take(1);
        }

        // Numbered items are already the source's own bullets, so they aren't split further.
        foreach (var piece in pieces)
            foreach (var part in piece.Split(';', StringSplitOptions.RemoveEmptyEntries))
                foreach (var duty in numbered ? [part] : SplitGerundList(part))
                    yield return duty;
    }

    /// <summary>
    /// Splits "coordinating X, assisting with Y, and developing Z" (or "preparing X and conducting Y")
    /// only when every piece is a whole "-ing" phrase of 2+ words, so "Designing, developing, and
    /// maintaining X" is never fragmented.
    /// </summary>
    private static IEnumerable<string> SplitGerundList(string part)
    {
        var pieces = Regex.Split(part.Trim(), @",\s*(?:and\s+|or\s+)?|\s+and\s+(?=[a-z]+ing\b)")
            .Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        var safe = pieces.Count >= 2 && pieces.All(p =>
            Regex.IsMatch(p, @"^[A-Za-z]+ing\b") && p.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2);
        return safe ? pieces : [part];
    }

    /// <summary>Offset just past the first sentence end (abbreviation-aware), or the text length.</summary>
    private static int SentenceEnd(string text)
    {
        foreach (Match m in Regex.Matches(text, @"[.!?](?=\s+[A-Z0-9(]|$)"))
        {
            var upto = text[..(m.Index + 1)];
            if (!P.Abbreviations.Any(a => upto.EndsWith(a, StringComparison.OrdinalIgnoreCase))) return m.Index + 1;
        }
        return text.Length;
    }

    /// <summary>Sentence split that doesn't break on "e.g.", "U.S.", "Ph.D." and similar.</summary>
    private static IEnumerable<string> Sentences(string text)
    {
        var start = 0;
        foreach (Match m in Regex.Matches(text, @"[.!?](?=\s+[A-Z0-9(]|$)"))
        {
            var upto = text[..(m.Index + 1)];
            if (P.Abbreviations.Any(a => upto.EndsWith(a, StringComparison.OrdinalIgnoreCase))) continue;
            var sentence = text[start..(m.Index + 1)].Trim();
            if (sentence.Length > 0) yield return sentence;
            start = m.Index + 1;
        }
        var tail = text[start..].Trim();
        if (tail.Length > 0) yield return tail;
    }

    /// <summary>"TQ 1: Title: text" blocks in SES/ST postings. Title only when a colon comes before the first period.</summary>
    private static List<TechnicalQualification> FindTechnicalQualifications(string text, Func<string?, string?> keep)
    {
        var labels = P.TechnicalQualification.Matches(text).Cast<Match>()
            .GroupBy(m => m.Groups["n"].Value).Select(g => g.First()).OrderBy(m => m.Index).ToList();
        var result = new List<TechnicalQualification>();
        for (var i = 0; i < labels.Count; i++)
        {
            var start = labels[i].Index + labels[i].Length;
            var end = i + 1 < labels.Count ? labels[i + 1].Index : text.Length;
            var segment = text[start..end];
            if (i == labels.Count - 1 && P.TechnicalQualificationStop.Match(segment) is { Success: true } stop) segment = segment[..stop.Index];
            if (segment.Length > 2000) segment = segment[..2000];

            string? title = null;
            var colon = segment.IndexOf(':');
            var period = segment.IndexOf(". ", StringComparison.Ordinal);
            if (colon is > 3 and < 130 && (period < 0 || colon < period))
            {
                title = keep(segment[..colon]);
                segment = segment[(colon + 1)..];
            }
            if (keep(segment) is { } body)
                result.Add(new TechnicalQualification { Label = $"TQ {labels[i].Groups["n"].Value}", Title = title, Text = body });
        }
        return result;
    }

    private static IEnumerable<string> FindNotes(string text)
    {
        // A URL often ends a sentence without a period, so it counts as a boundary too.
        var clauses = Regex.Split(text, @"https?://\S+")
            .SelectMany(Sentences).SelectMany(s => s.Split(';'))
            .Select(c => c.Trim()).Where(c => c.Length is > 10 and <= 280).ToList();
        foreach (var (_, pattern) in P.Notes)
            if (clauses.Where(c => pattern.IsMatch(c)).OrderBy(c => c.Length).FirstOrDefault() is { } note)
                yield return note;
    }

    /// <summary>
    /// The "OR Education:" path from the qualifications text, or else Details.Education when it
    /// holds a degree and a list of fields. Items are verified against whichever text they came from.
    /// </summary>
    private static EducationPath? FindEducation(string text, string? education, Func<string?, string, string?> keepIn)
    {
        string? raw = null;
        string? from = null;
        if (P.EducationPath.Match(text) is { Success: true } m)
        {
            raw = text[(m.Index + m.Length)..];
            var stop = P.DutyStop.Match(raw);
            if (stop.Success) raw = raw[..stop.Index];
            from = text;
        }
        else if (!string.IsNullOrWhiteSpace(education) && P.DegreeWords.IsMatch(education) && P.FieldsOfStudy.IsMatch(education))
        {
            raw = Regex.Replace(education, @"\s+", " ").Trim();
            from = raw;
        }
        if (string.IsNullOrWhiteSpace(raw) || from is null) return null;
        var source = Verbatim.Normalize(from);
        string? keep(string? span) => keepIn(span, source);

        var options = P.EducationAlternative.Split(raw.Trim()).Where(o => o.Trim().Length > 0).ToList();
        var first = options[0];
        var path = new EducationPath();

        if (P.FieldsOfStudy.Match(first) is { Success: true } fields)
        {
            path.Requirement = keep(first[..fields.Index]) ?? "";
            // Fields keep their source casing ("computer science"), so undo Tidy's capital letter.
            path.Fields = Regex.Split(fields.Groups["fields"].Value, @",\s*(?:or\s+|and\s+)?|\s+or\s+")
                .Select(f => f.Trim())
                .Where(f => f.Length > 0)
                .Select(f => keep(f) is { } kept ? f[0] + kept[1..] : null)
                .OfType<string>().ToList();
        }
        else
        {
            path.Requirement = keep(first) ?? "";
        }
        path.Alternatives = options.Skip(1).Select(keep).OfType<string>().ToList();
        return path.Requirement.Length > 0 ? path : null;
    }
}
