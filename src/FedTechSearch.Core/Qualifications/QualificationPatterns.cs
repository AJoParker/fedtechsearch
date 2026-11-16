using System.Text.RegularExpressions;

namespace FedTechSearch.Core.Qualifications;

/// <summary>
/// Every pattern the qualifications parser uses, in one place, so wording drift in USAJOBS
/// postings is fixed here without touching parser logic. Patterns only *locate* text; whatever
/// they find is copied verbatim and then checked by <see cref="Verbatim"/>.
/// </summary>
public static class QualificationPatterns
{
    private const RegexOptions Opts = RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>Starts a per-grade block: "To qualify at the GS-14 level", "For GS-12:", "Grade 13 To qualify…".</summary>
    public static readonly Regex GradeHeading = new(
        @"(?:Grade\s+\d{1,2}\s+)?To qualify (?:for|at) (?:the )?(?<plan>GS|GG)-(?<grade>\d{1,2})\b"
        + @"|\bFor (?<plan>GS|GG)-(?<grade>\d{1,2})\s*[:,]",
        Opts);

    /// <summary>A grade named in a section title, e.g. "IT SPECIALIST (DATA MANAGEMENT), GS-2210-11".</summary>
    public static readonly Regex SeriesGrade = new(@"\b(?<plan>GS|GG)-\d{4}-(?<grade>\d{1,2})\b", Opts);

    /// <summary>The grade the experience must be equivalent to.</summary>
    public static readonly Regex EquivalentGrade = new(
        @"lower grade(?:/level)? position in the federal service \((?<plan>GS|GG)-(?<grade>\d{1,2})\)"
        + @"|specialized experience at (?:or equivalent to )?(?:the )?(?<plan>GS|GG)-(?<grade>\d{1,2})\b"
        + @"|equivalent to (?:at least )?(?:the )?(?:next lower grade )?\(?(?<plan>GS|GG)-(?<grade>\d{1,2})\b",
        Opts);

    /// <summary>"One year of specialized experience", copied verbatim as the experience label.</summary>
    public static readonly Regex ExperienceLabel = new(@"\b(?:one|1) (?:full )?year of specialized experience", Opts);

    /// <summary>
    /// What introduces the duty list. The second branch is the bare "specialized experience:" form.
    /// "must include, or be supplemented by" is the competency preamble, not a duty list.
    /// </summary>
    public static readonly Regex DutyIntro = new(
        // Duties straight after the phrase, when the next word is an "-ing" verb:
        // "One year of specialized experience preparing …", "…at the GS-13 level, or equivalent, designing …".
        @"\b(?:one|1) (?:full )?year of specialized experience\s+(?:(?:in|with)\s+)?(?=[a-z]+ing\b)"
        + @"|specialized experience at (?:or equivalent to )?(?:the )?(?:GS|GG)-\d{1,2}(?: grade)?(?: level)?(?: or above)?,?\s*(?:or equivalent,?\s*)?(?=[a-z]+ing\b)"
        + @"|specialized experience\b[^.:;]{0,90}?\b(?:(?:which|that)\s+)?"
        + @"(?:includes?|is defined as|must include|must demonstrate(?: the following)?|may include(?: some or all| some)?(?: of the following)?|is described as|consists of|(?:would be|is|are|may be) demonstrated by)"
        + @"(?!,\s*or be supplemented)\s*:?\s*"
        + @"|specialized experience\b[^.:;]{0,90}?(?:level|equivalent|following|position)\s*:\s*",
        Opts);

    /// <summary>A duty list that starts with its first number: "1)", "(1)", "a)", "1.", "1 -".</summary>
    public static readonly Regex NumberedListStart = new(@"^(?:\(?(?:1|a)\)|1\.|1\s*[-–])\s", Opts);

    /// <summary>
    /// The number before each item (and any ";", "," or "and" before it). "1." and "1 -" need a space
    /// after them, so "v2.x" or "ICD-10" inside an item never count.
    /// </summary>
    public static readonly Regex NumberMarker = new(
        @"(?:^|[;,]?\s+(?:and\s+)?)(?:\(?(?:\d{1,2}|[a-h])\)|\d{1,2}\.|\d{1,2}\s*[-–])\s+", Opts);

    /// <summary>Where a duty list, or the competency boilerplate, stops.</summary>
    public static readonly Regex DutyStop = new(
        @"\bPLEASE NOTE\b|\bNOTE:|\bOR\b[^.]{0,20}Education\s*:|\bEducation\s*:|\bExperience refers to\b"
        + @"|\bAdditional qualification|\bQualification standards\b|\bYou may be asked\b|\bThis definition of specialized experience\b"
        + @"|\bThe specialized experience must\b|\bYour resume must\b|\bMinimum Qualifications\b|https?://|\bIn addition\b",
        Opts);

    /// <summary>Technical Qualifications in SES/ST postings: "TQ 1:", "TQ1.", "TQ 1 -".</summary>
    public static readonly Regex TechnicalQualification = new(@"\bTQ\s?(?<n>\d{1,2})\s*[:.\-–]\s*", Opts);

    /// <summary>Where the last TQ ends.</summary>
    public static readonly Regex TechnicalQualificationStop = new(
        @"\bEXECUTIVE CORE QUALIFICATIONS\b|\bECQ\s?\d|\bMandatory Technical Qualifications\b|\bNote:|\bPLEASE NOTE\b|\bHow (?:to|you will be) (?:apply|evaluated)\b",
        Opts);

    /// <summary>The five current Executive Core Qualifications (2025 OPM revision) and the earlier five, by name or phrase.</summary>
    public static readonly CompetencyFingerprint[] ExecutiveCore =
    [
        new("Commitment to the rule of law", new Regex(@"Commitment to the Rule of Law", Opts), ["american system of government", "uphold the constitution"]),
        new("Driving efficiency", new Regex(@"Driving Efficiency", Opts), ["cut wasteful spending", "manage resources, budget effectively"]),
        new("Merit and competence", new Regex(@"Merit and Competence", Opts), ["reliably produce work", "work that is of exceptional quality"]),
        new("Leading people", new Regex(@"Leading People", Opts), ["lead and inspire a group", "high-accountability culture"]),
        new("Achieving results", new Regex(@"Achieving Results", Opts), ["achieve both individual and organizational results", "align results to stated goals"]),
        new("Leading change", new Regex(@"Leading Change", Opts), ["bring about strategic change"]),
        new("Results driven", new Regex(@"Results Driven", Opts), ["meet organizational goals and customer expectations"]),
        new("Business acumen", new Regex(@"Business Acumen", Opts), ["manage human, financial, and information resources"]),
        new("Building coalitions", new Regex(@"Building Coalitions", Opts), ["build coalitions internally"]),
    ];

    /// <summary>The education alternative: "OR Education: …".</summary>
    public static readonly Regex EducationPath = new(@"\bOR\b[^.]{0,20}?Education\s*:\s*", Opts);

    /// <summary>A degree sentence, used to accept Details.Education as an education path.</summary>
    public static readonly Regex DegreeWords = new(@"\b(?:Ph\.?D|doctoral|bachelor'?s|master'?s|graduate education|degree)\b", Opts);

    /// <summary>"…institution in computer science, engineering, …, or technology management".</summary>
    public static readonly Regex FieldsOfStudy = new(@"\bin (?<fields>(?:[a-z][a-z/&\- ]{2,40}, ){2,}(?:or |and )?[a-z][a-z/&\- ]{2,40})", Opts);

    /// <summary>Splits the education path into alternatives ("…; or, three full years…").</summary>
    public static readonly Regex EducationAlternative = new(@";\s*or,?\s+", Opts);

    public static readonly Regex WhoMayApply = new(
        @"Who May Apply:\s*(?<value>[A-Z][\w.,'/\- ]{1,60}?)(?=\s+(?:In order|To qualify|Applicants|You|The|This|Experience|Basic|Specialized|Minimum|Qualifications?)\b|[.;:]|$)",
        Opts);

    /// <summary>A Who May Apply value must name an audience; "Only" (from "Only applicants who…") doesn't.</summary>
    public static readonly Regex Audience = new(
        @"\b(?:citizens?|employees?|candidates?|public|veterans?|spouses?|residents?|individuals|nationals|status|everyone)\b", Opts);

    /// <summary>Notes worth surfacing; the shortest matching clause is kept verbatim.</summary>
    public static readonly (string Name, Regex Pattern)[] Notes =
    [
        // Says volunteer/unpaid experience counts, not just that volunteering is good for you.
        ("volunteer", new Regex(@"\b(?:credit for|refers to|includ\w*)\b[^.;]{0,120}\b(?:volunteer|unpaid)\b|\bpaid (?:or|and) unpaid\b", Opts)),
        ("resume-length", new Regex(@"\b(?:not to exceed|exceeding|over the|limited to)\s+(?:\d|two|three)[- ]pages?\b", Opts)),
        ("ecq-exempt", new Regex(@"\bdo not need to (?:respond to|address) the ECQs\b", Opts)),
        ("transcripts", new Regex(@"\btranscripts?\b[^.;]{0,80}\brequired\b|\brequired\b[^.;]{0,80}\btranscripts?\b", Opts)),
    ];

    /// <summary>Abbreviations that end with a period but don't end a sentence.</summary>
    public static readonly string[] Abbreviations = ["e.g.", "i.e.", "etc.", "U.S.", "Ph.D.", "No.", "vs.", "Inc.", "Dr.", "St."];

    /// <summary>The standard OPM 2210 competencies: a heading, or two distinctive phrases if the heading drifted.</summary>
    public static readonly CompetencyFingerprint[] Competencies =
    [
        new("Attention to detail", new Regex(@"(?:\(\d\)\s*)?Attention to Detail\s*[-–:]", Opts),
            ["thorough when performing work", "conscientious about attending to detail", "rarely requires editing"]),
        new("Customer service", new Regex(@"(?:\(\d\)\s*)?Customer Service\s*[-–:]", Opts),
            ["works with clients and customers", "assess their needs", "satisfy their expectations"]),
        new("Oral communication", new Regex(@"(?:\(\d\)\s*)?Oral Communication\s*[-–:]", Opts),
            ["expresses information", "convincing oral presentations", "attends to nonverbal cues"]),
        new("Problem solving", new Regex(@"(?:\(\d\)\s*)?Problem Solving\s*[-–:]", Opts),
            ["identifies problems", "determines accuracy and relevance of information", "generate and evaluate alternatives"]),
    ];
}

public sealed record CompetencyFingerprint(string Name, Regex Heading, string[] Phrases);
