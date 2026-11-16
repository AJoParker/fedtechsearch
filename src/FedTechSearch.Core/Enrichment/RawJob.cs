namespace FedTechSearch.Core.Enrichment;

/// <summary>A duty location with coordinates. City is the town ("Arlington" for "Pentagon, Arlington, Virginia").</summary>
public sealed record GeoLocation(string Name, string City, string State, string Country, double Latitude, double Longitude);

/// <summary>
/// A job announcement normalized out of the USAJOBS response, before any enrichment.
/// </summary>
public sealed class RawJob
{
    public string Id { get; set; } = "";
    public string AnnouncementNumber { get; set; } = "";
    public string Title { get; set; } = "";
    public string Agency { get; set; } = "";
    public string Department { get; set; } = "";
    public string? SubAgency { get; set; }
    public string Url { get; set; } = "";
    public string? ApplyUrl { get; set; }
    public string LocationDisplay { get; set; } = "";
    public List<string> Locations { get; set; } = [];
    public List<string> States { get; set; } = [];

    /// <summary>Duty locations that have real coordinates (placeholders and 0/0 are left out).</summary>
    public List<GeoLocation> GeoLocations { get; set; } = [];
    public List<string> Series { get; set; } = [];
    public string? PayPlan { get; set; }
    public int? LowGrade { get; set; }
    public int? HighGrade { get; set; }
    public decimal? SalaryMin { get; set; }
    public decimal? SalaryMax { get; set; }
    public string? SalaryInterval { get; set; }
    public DateTime? OpenDate { get; set; }
    public DateTime? CloseDate { get; set; }
    public string Summary { get; set; } = "";
    public List<string> Duties { get; set; } = [];
    public string Qualifications { get; set; } = "";
    public string Requirements { get; set; } = "";
    public string? WhoMayApply { get; set; }
    public List<string> HiringPaths { get; set; } = [];
    public bool? TeleworkEligible { get; set; }
    public bool? RemoteIndicator { get; set; }
    public string? SecurityClearance { get; set; }
    public string? Schedule { get; set; }
    public string? OfferingType { get; set; }
    public string? ScheduleType { get; set; }
    public string? AppointmentType { get; set; }
    public string? TravelRequirement { get; set; }
    public int? PromotionPotential { get; set; }
    public string HowToApply { get; set; } = "";

    /// <summary>The text the role and tech rules read: title, summary, duties and qualifications.</summary>
    public string AnalysisText() =>
        string.Join("\n", new[] { Title, Summary }.Concat(Duties).Append(Qualifications)
            .Where(s => !string.IsNullOrWhiteSpace(s)));
}
