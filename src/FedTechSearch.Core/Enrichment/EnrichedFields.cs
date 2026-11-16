namespace FedTechSearch.Core.Enrichment;

public static class Seniority
{
    public const string Entry = "Entry";
    public const string Junior = "Junior";
    public const string Mid = "Mid";
    public const string Senior = "Senior";
    public const string Lead = "Lead";
    public const string Principal = "Principal";

    public static readonly IReadOnlyList<string> All = [Entry, Junior, Mid, Senior, Lead, Principal];
}

public static class WorkArrangement
{
    public const string Remote = "Remote";
    public const string Telework = "Telework eligible";
    public const string OnSite = "On-site";

    public static readonly IReadOnlyList<string> All = [Remote, Telework, OnSite];
}

public static class Clearance
{
    public const string None = "None";
    public const string PublicTrust = "Public Trust";
    public const string Confidential = "Confidential";
    public const string Secret = "Secret";
    public const string TopSecret = "Top Secret";
    public const string TsSci = "TS/SCI";
    public const string DoeQL = "DOE Q/L";

    public static readonly IReadOnlyList<string> All = [None, PublicTrust, Confidential, Secret, TopSecret, TsSci, DoeQL];
}

/// <summary>
/// USAJOBS hiring path codes in plain language. The four federal-employee codes share one
/// label so the facet stays short. The label is the filter value.
/// </summary>
public static class HiringPath
{
    public const string FederalEmployees = "Federal employees";

    private static readonly (string Label, string[] Codes)[] Map =
    [
        ("Open to the public", ["public"]),
        ("Veterans", ["vet"]),
        ("Military spouses", ["mspouse"]),
        ("Individuals with disabilities (Schedule A)", ["disability"]),
        (FederalEmployees, ["fed-competitive", "fed-excepted", "fed-internal-search", "fed-transition"]),
        ("Students", ["student"]),
        ("Recent graduates", ["graduates"]),
        ("National Guard and Reserves", ["nguard"]),
        ("Native Americans", ["native"]),
        ("Peace Corps and AmeriCorps alumni", ["peace"]),
        ("Land and base management", ["land"]),
        ("Family of overseas employees", ["overseas"]),
        ("Senior executives", ["ses"]),
        ("Special authorities", ["special-authorities"]),
    ];

    public static readonly IReadOnlyList<string> All = Map.Select(m => m.Label).ToList();

    /// <summary>Unknown codes fall back to the raw code so nothing is silently dropped.</summary>
    public static string LabelFor(string code) =>
        Map.FirstOrDefault(m => m.Codes.Contains(code, StringComparer.OrdinalIgnoreCase)).Label ?? code;

    public static IReadOnlyList<string> CodesFor(string label) =>
        Map.FirstOrDefault(m => m.Label.Equals(label, StringComparison.OrdinalIgnoreCase)).Codes ?? [label];

    public static List<string> LabelsFor(IEnumerable<string> codes) => codes.Select(LabelFor).Distinct().ToList();
}

/// <summary>USAJOBS PositionSchedule codes (codelist/positionscheduletypes). Names are often blank; codes are not.</summary>
public static class ScheduleType
{
    public const string FullTime = "Full-time";
    public const string PartTime = "Part-time";
    public const string ShiftWork = "Shift work";
    public const string Intermittent = "Intermittent";
    public const string JobSharing = "Job sharing";
    public const string Multiple = "Multiple schedules";

    public static readonly IReadOnlyList<string> All = [FullTime, PartTime, ShiftWork, Intermittent, JobSharing, Multiple];

    public static string? FromCode(string? code) => code?.Trim() switch
    {
        "1" => FullTime, "2" => PartTime, "3" => ShiftWork, "4" => Intermittent, "5" => JobSharing, "6" => Multiple,
        _ => null
    };
}

/// <summary>USAJOBS PositionOfferingType codes (codelist/positionofferingtypes), grouped into a short list.</summary>
public static class AppointmentType
{
    public const string Permanent = "Permanent";
    public const string Term = "Term";
    public const string Temporary = "Temporary";
    public const string Detail = "Detail or temporary promotion";
    public const string Internship = "Internship or recent graduates";
    public const string Multiple = "Multiple";
    public const string Other = "Other";

    public static readonly IReadOnlyList<string> All = [Permanent, Term, Temporary, Detail, Internship, Multiple, Other];

    public static string? FromCode(string? code) => code?.Trim() switch
    {
        null or "" => null,
        "15317" => Permanent,
        "15319" => Term,
        "15318" or "15322" or "15323" or "15522" => Temporary, // temporary, seasonal, summer, intermittent
        "15320" or "15321" => Detail,
        "15324" or "15326" or "15328" => Internship,           // PMF, recent graduates, internships
        "15327" => Multiple,
        _ => Other                                              // ICTAP only, agency employees only, telework
    };
}

/// <summary>USAJOBS TravelCode values (codelist/travelpercentages).</summary>
public static class TravelRequirement
{
    public const string None = "Not required";
    public const string Occasional = "Occasional";
    public const string UpTo25 = "25% or less";
    public const string UpTo50 = "50% or less";
    public const string UpTo75 = "75% or less";
    public const string Over75 = "76% or more";

    public static readonly IReadOnlyList<string> All = [None, Occasional, UpTo25, UpTo50, UpTo75, Over75];

    public static string? FromCode(string? code) => code?.Trim() switch
    {
        "0" => None, "1" => Occasional, "2" => UpTo25, "3" or "5" => UpTo50, "7" => UpTo75, "8" => Over75,
        _ => null
    };
}

public static class WorkingTitleSource
{
    public const string Posting = "posting";
    public const string Rules = "rules";
    public const string Llm = "llm";
}

public sealed record RoleScore(string Family, int Score);

/// <summary>The searchable fields derived from a raw job.</summary>
public sealed class EnrichedFields
{
    public List<string> SpecialtyCodes { get; set; } = [];
    public List<string> Specialties { get; set; } = [];
    public string RoleFamily { get; set; } = "";
    public double RoleConfidence { get; set; }
    public List<RoleScore> TopScores { get; set; } = [];
    public string WorkingTitle { get; set; } = "";
    public string WorkingTitleSource { get; set; } = Enrichment.WorkingTitleSource.Rules;
    public List<string> Tech { get; set; } = [];
    public List<string> Seniority { get; set; } = [];
    public bool Supervisory { get; set; }
    public string WorkArrangement { get; set; } = Enrichment.WorkArrangement.OnSite;
    public string Clearance { get; set; } = Enrichment.Clearance.None;
    public bool DirectHire { get; set; }
    public bool OpenToPublic { get; set; }

    /// <summary>Readable qualifications: every string a verbatim span of the source text.</summary>
    public Qualifications.QualificationInfo Qualifications { get; set; } = new();

    /// <summary>Items the parser found but the verbatim check rejected; logged at ingest for tuning.</summary>
    public List<string> QualificationRejected { get; set; } = [];

    /// <summary>The posting says it may close before its closing date (e.g. "first 100 applicants").</summary>
    public bool MayCloseEarly { get; set; }

    /// <summary>True when the rule-based role guess is weak and an LLM second opinion is worth it.</summary>
    public bool LowConfidence { get; set; }
}
