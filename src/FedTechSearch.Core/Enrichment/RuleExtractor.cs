using System.Text.RegularExpressions;
using FedTechSearch.Core.Taxonomy;

namespace FedTechSearch.Core.Enrichment;

public sealed class RuleExtractorOptions
{
    /// <summary>Below this score the job is filed as General IT.</summary>
    public int MinScore { get; set; } = 3;

    /// <summary>A winning score below this is treated as low confidence.</summary>
    public int StrongScore { get; set; } = 5;

    /// <summary>Winner share of (winner + runner-up) below this is treated as low confidence.</summary>
    public double MinConfidence { get; set; } = 0.65;
}

/// <summary>
/// Deterministic, free extraction of the searchable fields. Runs on every job.
/// </summary>
public sealed class RuleExtractor
{
    private const int SpecialtyWeight = 3;
    private const int SeriesWeight = 3;
    private const int TitleWeight = 4;
    private const int TextCap = 6;

    private static readonly HashSet<string> GsLikePayPlans = new(StringComparer.OrdinalIgnoreCase) { "GS", "GG", "GL", "GM" };

    private static readonly Regex Parenthetical = new(@"\(([^)]+)\)", RegexOptions.Compiled);
    private static readonly Regex OfficialTitle = new(
        @"official (?:position )?title of this position is[^(\n]*\(([^)]+)\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SpecialtySplit = new(@"[/,;&]", RegexOptions.Compiled);

    // "serves as a Full Stack Software Developer on ..." -> "Full Stack Software Developer"
    private static readonly Regex ServeAs = new(
        @"\b[Ss]erv(?:e|es|ing) as (?:an?|the) ((?:[A-Z][\w/&+.\-]*)(?:\s+(?:[A-Z][\w/&+.\-]*|of|and|for|&))*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TrailingConnector = new(@"\s+(?:of|and|for|&)$", RegexOptions.Compiled);
    private static readonly Regex RoleNoun = new(
        @"(?:engineer|developer|programmer|analyst|architect|specialist|administrator|manager|scientist|lead|officer|designer|consultant|technician|strategist|owner|master|director|chief)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex GenericFederalTitle = new(
        @"^(?:IT|Information Technology)(?: Cybersecurity)? Specialist$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex SupervisoryTitle = new(@"\bsupervisory\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SupervisoryText = new(
        @"\bsupervis(?:es|ing) (?:a )?(?:staff|team|employees|personnel)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DirectHireText = new(@"\bdirect[\s-]hire\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // Applicant caps, e.g. "limited to the first 100 applicants". Not the routine "by the
    // cut-off/closing date of the announcement" eligibility wording.
    private static readonly Regex MayCloseEarlyText = new(
        @"\bfirst\s+\d+\s+(?:applications|applicants|candidates)\b"
        + @"|\bmay\s+close\s+(?:early|before)\b"
        + @"|\b(?:will|may)\s+close\s+(?:once|when|after)\b[^.]{0,60}\b\d+\s+(?:applications|applicants)\b"
        + @"|\bapplication\s+(?:limit|cap)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RemoteText = new(@"\bremote job\b|\bremote work\b|\bfully remote\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly CompiledTaxonomy _taxonomy;
    private readonly RuleExtractorOptions _options;

    public RuleExtractor(CompiledTaxonomy taxonomy, RuleExtractorOptions? options = null)
    {
        _taxonomy = taxonomy;
        _options = options ?? new RuleExtractorOptions();
    }

    public EnrichedFields Extract(RawJob job)
    {
        var text = job.AnalysisText();
        var fields = new EnrichedFields();

        fields.SpecialtyCodes = ExtractSpecialties(job.Title, text);
        fields.Specialties = fields.SpecialtyCodes
            .Select(c => _taxonomy.SpecialtyNameByCode.TryGetValue(c, out var n) ? n : c)
            .ToList();

        ScoreRoles(job, text, fields);

        fields.Tech = _taxonomy.Tech
            .Where(t => t.Patterns.Any(p => p.IsMatch(text)))
            .Select(t => t.Definition.Name)
            .ToList();

        fields.Seniority = ComputeSeniority(job);
        fields.Supervisory = SupervisoryTitle.IsMatch(job.Title) || SupervisoryText.IsMatch(text);
        fields.WorkArrangement = ComputeWorkArrangement(job);
        fields.Clearance = NormalizeClearance(job.SecurityClearance, text);
        fields.DirectHire = DirectHireText.IsMatch(job.Title) || DirectHireText.IsMatch(text) || DirectHireText.IsMatch(job.Requirements);
        fields.MayCloseEarly = MayCloseEarlyText.IsMatch(text) || MayCloseEarlyText.IsMatch(job.Requirements)
                               || MayCloseEarlyText.IsMatch(job.HowToApply);
        fields.OpenToPublic = job.HiringPaths.Any(p => p.Equals("public", StringComparison.OrdinalIgnoreCase))
                              || (job.WhoMayApply?.Contains("public", StringComparison.OrdinalIgnoreCase) ?? false);

        (fields.WorkingTitle, fields.WorkingTitleSource) = ComputeWorkingTitle(job, fields);
        return fields;
    }

    public List<string> ExtractSpecialties(string title, string text)
    {
        var codes = new List<string>();
        foreach (Match m in Parenthetical.Matches(title))
            AddSpecialtyCodes(m.Groups[1].Value, codes);

        if (codes.Count == 0)
        {
            var official = OfficialTitle.Match(text);
            if (official.Success) AddSpecialtyCodes(official.Groups[1].Value, codes);
        }
        return codes.Distinct().ToList();
    }

    private void AddSpecialtyCodes(string group, List<string> codes)
    {
        var whole = CompiledTaxonomy.NormalizeSpecialtyToken(group);
        if (_taxonomy.SpecialtyCodeByAlias.TryGetValue(whole, out var code))
        {
            codes.Add(code);
            return;
        }
        foreach (var part in SpecialtySplit.Split(whole))
        {
            if (_taxonomy.SpecialtyCodeByAlias.TryGetValue(part.Trim(), out var partCode))
                codes.Add(partCode);
        }
    }

    private void ScoreRoles(RawJob job, string text, EnrichedFields fields)
    {
        // Parentheticals are already counted through the specialty weight, so leave them out of title matching.
        var bareTitle = Parenthetical.Replace(job.Title, " ");

        var scores = _taxonomy.RoleFamilies
            .Select(f =>
            {
                var def = f.Definition;
                var score = 0;
                if (def.Specialties.Any(fields.SpecialtyCodes.Contains)) score += SpecialtyWeight;
                if (def.Series.Any(job.Series.Contains)) score += SeriesWeight;
                score += TitleWeight * f.TitlePatterns.Count(p => p.IsMatch(bareTitle));
                score += Math.Min(TextCap, f.TextPatterns.Count(p => p.IsMatch(text)));
                return new RoleScore(def.Name, score);
            })
            .OrderByDescending(s => s.Score) // stable: ties keep taxonomy order
            .ToList();

        fields.TopScores = scores.Take(3).ToList();
        var best = scores.FirstOrDefault();
        var second = scores.Skip(1).FirstOrDefault();

        if (best is null || best.Score < _options.MinScore)
        {
            fields.RoleFamily = CompiledTaxonomy.GeneralFamily;
            fields.RoleConfidence = 0;
            fields.LowConfidence = true;
            return;
        }

        fields.RoleFamily = best.Family;
        fields.RoleConfidence = (double)best.Score / (best.Score + (second?.Score ?? 0));
        fields.LowConfidence = best.Score < _options.StrongScore || fields.RoleConfidence < _options.MinConfidence;
    }

    public static List<string> ComputeSeniority(RawJob job)
    {
        int lo, hi;
        if (job.PayPlan is not null && GsLikePayPlans.Contains(job.PayPlan) && (job.LowGrade ?? job.HighGrade) is not null)
        {
            lo = GradeLevel(job.LowGrade ?? job.HighGrade!.Value);
            hi = GradeLevel(job.HighGrade ?? job.LowGrade!.Value);
        }
        else if ((job.SalaryMin ?? job.SalaryMax) is not null && IsAnnual(job.SalaryInterval))
        {
            lo = SalaryLevel(job.SalaryMin ?? job.SalaryMax!.Value);
            hi = SalaryLevel(job.SalaryMax ?? job.SalaryMin!.Value);
        }
        else
        {
            return [];
        }

        if (hi < lo) (lo, hi) = (hi, lo);
        return Enrichment.Seniority.All.Skip(lo).Take(hi - lo + 1).ToList();
    }

    private static bool IsAnnual(string? interval) =>
        interval is null || interval.Equals("PA", StringComparison.OrdinalIgnoreCase)
                         || interval.Contains("year", StringComparison.OrdinalIgnoreCase);

    private static int GradeLevel(int grade) => grade switch
    {
        <= 7 => 0,
        <= 10 => 1,
        <= 12 => 2,
        13 => 3,
        14 => 4,
        _ => 5
    };

    private static int SalaryLevel(decimal salary) => salary switch
    {
        < 70_000m => 0,
        < 90_000m => 1,
        < 120_000m => 2,
        < 150_000m => 3,
        < 180_000m => 4,
        _ => 5
    };

    private static string ComputeWorkArrangement(RawJob job)
    {
        if (job.RemoteIndicator == true || RemoteText.IsMatch(job.LocationDisplay))
            return WorkArrangement.Remote;
        if (job.TeleworkEligible == true)
            return WorkArrangement.Telework;
        return WorkArrangement.OnSite;
    }

    public static string NormalizeClearance(string? raw, string text)
    {
        if (!string.IsNullOrWhiteSpace(raw))
        {
            var r = raw.ToLowerInvariant();
            if (r.Contains("sci")) return Clearance.TsSci;
            if (r.Contains("top secret")) return Clearance.TopSecret;
            if (r.Contains("secret")) return Clearance.Secret;
            if (r.Contains("confidential")) return Clearance.Confidential;
            if (Regex.IsMatch(r, @"^\s*[ql]\b|\bq[\s-]|\bl[\s-]sensitive|\bq sensitive")) return Clearance.DoeQL;
            if (r.Contains("public trust")) return Clearance.PublicTrust;
            if (r.Contains("not required") || r.Contains("none") || r.Contains("not applicable")) return Clearance.None;
        }

        // No usable structured value: fall back to the posting text.
        if (Regex.IsMatch(text, @"\bTS/SCI\b|Top Secret/(?:SCI|Sensitive Compartmented)", RegexOptions.IgnoreCase)) return Clearance.TsSci;
        if (Regex.IsMatch(text, @"\bTop Secret\b", RegexOptions.IgnoreCase)) return Clearance.TopSecret;
        if (Regex.IsMatch(text, @"\bSecret (?:security )?clearance\b", RegexOptions.IgnoreCase)) return Clearance.Secret;
        if (Regex.IsMatch(text, @"\bpublic trust\b", RegexOptions.IgnoreCase)) return Clearance.PublicTrust;
        return Clearance.None;
    }

    private (string Title, string Source) ComputeWorkingTitle(RawJob job, EnrichedFields fields)
    {
        var fromPosting = FindServeAsTitle(job.Summary) ?? job.Duties.Select(FindServeAsTitle).FirstOrDefault(t => t is not null);
        if (fromPosting is not null)
            return (fromPosting, WorkingTitleSource.Posting);

        var family = _taxonomy.RoleFamilies.FirstOrDefault(f => f.Definition.Name == fields.RoleFamily);
        var baseTitle = family?.Definition.WorkingTitle ?? CompiledTaxonomy.GeneralWorkingTitle;
        return (WithLevelPrefix(baseTitle, fields), WorkingTitleSource.Rules);
    }

    public static string WithLevelPrefix(string baseTitle, EnrichedFields fields)
    {
        if (fields.Supervisory) return $"Supervisory {baseTitle}";
        var top = fields.Seniority.LastOrDefault();
        return top switch
        {
            Enrichment.Seniority.Senior or Enrichment.Seniority.Lead or Enrichment.Seniority.Principal => $"{top} {baseTitle}",
            _ => baseTitle
        };
    }

    public static string? FindServeAsTitle(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        foreach (Match m in ServeAs.Matches(text))
        {
            var candidate = TrailingConnector.Replace(m.Groups[1].Value.Trim(), "").TrimEnd('.', ',');
            if (candidate.Length is < 4 or > 80) continue;
            if (!RoleNoun.IsMatch(candidate)) continue;
            if (GenericFederalTitle.IsMatch(candidate)) continue;
            return candidate;
        }
        return null;
    }
}
