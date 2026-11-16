namespace FedTechSearch.Core.Model;

/// <summary>
/// A stored, enriched job. Structured USAJOBS fields plus the derived search fields.
/// </summary>
public sealed class Job
{
    // Identity and links
    public string Id { get; set; } = "";
    public string AnnouncementNumber { get; set; } = "";
    public string Url { get; set; } = "";
    public string? ApplyUrl { get; set; }

    // Straight from USAJOBS
    public string OfficialTitle { get; set; } = "";
    public string Agency { get; set; } = "";
    public string Department { get; set; } = "";
    public string? SubAgency { get; set; }
    public string LocationDisplay { get; set; } = "";
    public List<string> Locations { get; set; } = [];
    public List<string> States { get; set; } = [];
    public List<string> Series { get; set; } = [];
    public string? PayPlan { get; set; }
    public int? LowGrade { get; set; }
    public int? HighGrade { get; set; }
    public decimal? SalaryMin { get; set; }
    public decimal? SalaryMax { get; set; }
    public string? SalaryInterval { get; set; }
    public DateTime? OpenDate { get; set; }
    public DateTime? CloseDate { get; set; }
    public string? Schedule { get; set; }
    public string? OfferingType { get; set; }
    public string? ScheduleType { get; set; }
    public string? AppointmentType { get; set; }
    public string? TravelRequirement { get; set; }
    public int? PromotionPotential { get; set; }
    public List<string> HiringPaths { get; set; } = [];
    public string Summary { get; set; } = "";
    public List<string> Duties { get; set; } = [];
    public string Qualifications { get; set; } = "";

    // Derived search fields
    public string WorkingTitle { get; set; } = "";
    public string WorkingTitleSource { get; set; } = "";
    public string RoleFamily { get; set; } = "";
    public double RoleConfidence { get; set; }
    public List<string> SpecialtyCodes { get; set; } = [];
    public List<string> Specialties { get; set; } = [];
    public List<string> Tech { get; set; } = [];
    public List<string> Seniority { get; set; } = [];
    public bool Supervisory { get; set; }
    public string WorkArrangement { get; set; } = "";
    public string Clearance { get; set; } = "";
    public bool DirectHire { get; set; }
    public bool OpenToPublic { get; set; }
    public bool MayCloseEarly { get; set; }

    /// <summary>Concatenated text indexed by Postgres full-text search.</summary>
    public string SearchText { get; set; } = "";

    // Bookkeeping
    public bool IsActive { get; set; } = true;
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public string ContentHash { get; set; } = "";
    public int EnrichmentVersion { get; set; }
}
