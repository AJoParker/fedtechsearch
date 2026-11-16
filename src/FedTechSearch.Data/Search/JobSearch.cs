using System.Linq.Expressions;
using FedTechSearch.Core.Enrichment;
using FedTechSearch.Core.Model;
using FedTechSearch.Core.Search;
using Microsoft.EntityFrameworkCore;

namespace FedTechSearch.Data.Search;

/// <summary>
/// Query-string shaped search request. Repeat a key for several values, e.g.
/// ?tech=.NET&amp;tech=Azure&amp;roleFamily=DevOps%20%26%20Cloud
/// </summary>
public sealed class JobSearchRequest
{
    public string? Q { get; set; }

    /// <summary>Free-text place, e.g. "Houston, TX, US". Remote jobs are included unless IncludeRemote is false.</summary>
    public string? Location { get; set; }

    public bool? IncludeRemote { get; set; }
    public string[]? RoleFamily { get; set; }
    public string[]? Tech { get; set; }
    public string[]? Specialty { get; set; }
    public string[]? Seniority { get; set; }
    public string[]? Work { get; set; }
    public string[]? Clearance { get; set; }
    public string[]? State { get; set; }
    public string[]? Agency { get; set; }
    public string[]? Series { get; set; }
    public decimal? MinSalary { get; set; }
    public bool? DirectHire { get; set; }
    public bool? OpenToPublic { get; set; }
    public bool? Supervisory { get; set; }
    public bool? IncludeClosed { get; set; }

    /// <summary>Opened in the last n days: 1, 3, 7 or 14. Other values are ignored.</summary>
    public int? PostedWithinDays { get; set; }

    /// <summary>Closes within n days: 3 or 7. Other values are ignored.</summary>
    public int? ClosingWithinDays { get; set; }

    public bool? MayCloseEarly { get; set; }

    /// <summary>Plain-language labels from <see cref="Core.Enrichment.HiringPath"/>, OR'ed.</summary>
    public string[]? HiringPath { get; set; }

    public string[]? Department { get; set; }
    public string[]? Schedule { get; set; }
    public string[]? Appointment { get; set; }
    public string[]? Travel { get; set; }

    /// <summary>GS-style grade range; jobs whose grade span overlaps it. Non-GS pay plans drop out.</summary>
    public int? MinGrade { get; set; }
    public int? MaxGrade { get; set; }

    /// <summary>Career ladder ceiling, e.g. 13 means the job can grow to GS-13 or higher.</summary>
    public int? MinPromotionPotential { get; set; }

    public string[]? ExcludeAgency { get; set; }
    public string[]? ExcludeTech { get; set; }

    /// <summary>Words to leave out (web-search syntax, matched against the full text).</summary>
    public string? ExcludeKeyword { get; set; }

    /// <summary>relevance (default with q), newest (default without q), closing, salary</summary>
    public string? Sort { get; set; }

    public int? Page { get; set; }
    public int? PageSize { get; set; }
}

public sealed record JobListItem(
    string Id,
    string WorkingTitle,
    string OfficialTitle,
    string RoleFamily,
    string Agency,
    string Department,
    string LocationDisplay,
    IReadOnlyList<string> Tech,
    IReadOnlyList<string> Seniority,
    IReadOnlyList<string> Specialties,
    string WorkArrangement,
    string Clearance,
    string? PayPlan,
    int? LowGrade,
    int? HighGrade,
    decimal? SalaryMin,
    decimal? SalaryMax,
    DateTime? OpenDate,
    DateTime? CloseDate,
    bool DirectHire,
    bool OpenToPublic,
    bool Supervisory,
    bool MayCloseEarly,
    IReadOnlyList<string> HiringPaths,
    int? PromotionPotential,
    DateTime FirstSeen,
    string Url,
    string Snippet);

public sealed record FacetValue(string Value, int Count);

public sealed record JobSearchResponse(
    int Total,
    int Page,
    int PageSize,
    IReadOnlyList<JobListItem> Items,
    IReadOnlyDictionary<string, IReadOnlyList<FacetValue>> Facets);

public sealed class JobSearch(AppDbContext db, TimeProvider time)
{
    private const string FtsConfig = "english";
    public const string IndividualContributor = "Individual contributor";
    public const string PeopleManager = "People manager";

    /// <summary>Pay plans that use the General Schedule grade numbers the grade filter speaks.</summary>
    private static readonly string[] GsPayPlans = ["GS", "GG", "GL", "GM"];

    public async Task<JobSearchResponse> SearchAsync(JobSearchRequest r, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, r.Page ?? 1);
        var pageSize = Math.Clamp(r.PageSize ?? 20, 1, 100);
        var q = Filter(db.Jobs.AsNoTracking(), r);

        // Facets are counted over the filtered set. Federal tech postings number in the low
        // thousands, so counting in memory over a narrow projection is cheap and simple.
        var facetRows = await q.Select(j => new
        {
            j.RoleFamily, j.Tech, j.Seniority, j.WorkArrangement, j.Clearance, j.States,
            j.Agency, j.Specialties, j.Series, j.DirectHire, j.Supervisory, j.MayCloseEarly,
            j.HiringPaths, j.Department, j.ScheduleType, j.AppointmentType, j.TravelRequirement, j.PromotionPotential
        }).ToListAsync(cancellationToken);

        var facets = new Dictionary<string, IReadOnlyList<FacetValue>>
        {
            ["roleFamily"] = Count(facetRows.Select(x => x.RoleFamily)),
            ["tech"] = Count(facetRows.SelectMany(x => x.Tech), 80),
            ["seniority"] = InOrder(Count(facetRows.SelectMany(x => x.Seniority)), Seniority.All),
            ["work"] = InOrder(Count(facetRows.Select(x => x.WorkArrangement)), WorkArrangement.All),
            ["clearance"] = InOrder(Count(facetRows.Select(x => x.Clearance)), Clearance.All),
            ["state"] = Count(facetRows.SelectMany(x => x.States)),
            ["agency"] = Count(facetRows.Select(x => x.Agency), 50),
            ["specialty"] = Count(facetRows.SelectMany(x => x.Specialties)),
            ["series"] = Count(facetRows.SelectMany(x => x.Series)),
            ["flags"] = Count(facetRows.SelectMany(x => Flags(x.DirectHire, x.MayCloseEarly))),
            ["hiringPath"] = InOrder(Count(facetRows.SelectMany(x => HiringPath.LabelsFor(x.HiringPaths))), HiringPath.All),
            ["department"] = Count(facetRows.Select(x => x.Department)),
            ["schedule"] = InOrder(Count(facetRows.Select(x => x.ScheduleType ?? "")), ScheduleType.All),
            ["appointment"] = InOrder(Count(facetRows.Select(x => x.AppointmentType ?? "")), AppointmentType.All),
            ["travel"] = InOrder(Count(facetRows.Select(x => x.TravelRequirement ?? "")), TravelRequirement.All),
            ["roleType"] = InOrder(Count(facetRows.Select(x => x.Supervisory ? PeopleManager : IndividualContributor)),
                [IndividualContributor, PeopleManager]),
            ["promotionPotential"] = Count(facetRows.Where(x => x.PromotionPotential != null)
                .Select(x => x.PromotionPotential!.Value.ToString())).OrderBy(f => int.Parse(f.Value)).ToList()
        };

        var sorted = Sort(q, r);
        var rows = await sorted
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(j => new
            {
                j.Id, j.WorkingTitle, j.OfficialTitle, j.RoleFamily, j.Agency, j.Department, j.LocationDisplay,
                j.Tech, j.Seniority, j.Specialties, j.WorkArrangement, j.Clearance, j.PayPlan, j.LowGrade,
                j.HighGrade, j.SalaryMin, j.SalaryMax, j.OpenDate, j.CloseDate, j.DirectHire, j.OpenToPublic,
                j.Supervisory, j.MayCloseEarly, j.HiringPaths, j.PromotionPotential, j.FirstSeen, j.Url, j.Summary
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(j => new JobListItem(
            j.Id, j.WorkingTitle, j.OfficialTitle, j.RoleFamily, j.Agency, j.Department, j.LocationDisplay,
            j.Tech, j.Seniority, j.Specialties, j.WorkArrangement, j.Clearance, j.PayPlan, j.LowGrade,
            j.HighGrade, j.SalaryMin, j.SalaryMax, j.OpenDate, j.CloseDate, j.DirectHire, j.OpenToPublic,
            j.Supervisory, j.MayCloseEarly, HiringPath.LabelsFor(j.HiringPaths), j.PromotionPotential, j.FirstSeen,
            j.Url, Truncate(j.Summary, 280))).ToList();

        return new JobSearchResponse(facetRows.Count, page, pageSize, items, facets);
    }

    public Task<Job?> GetAsync(string id, CancellationToken cancellationToken) =>
        db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

    private IQueryable<Job> Filter(IQueryable<Job> q, JobSearchRequest r)
    {
        if (r.IncludeClosed != true) q = q.Where(j => j.IsActive);

        if (!string.IsNullOrWhiteSpace(r.Q))
        {
            var text = r.Q.Trim();
            q = q.Where(j => EF.Functions.ToTsVector(FtsConfig, j.SearchText)
                .Matches(EF.Functions.WebSearchToTsQuery(FtsConfig, text)));
        }

        if (LocationQuery.Parse(r.Location) is { } loc)
            q = q.Where(Located(loc, r.IncludeRemote != false));

        // Within a facet values are OR'ed ("any of"), except tech, which is AND'ed:
        // ".NET + Azure" should mean jobs that ask for both.
        if (Has(r.RoleFamily, out var families)) q = q.Where(j => families.Contains(j.RoleFamily));
        if (Has(r.Work, out var work)) q = q.Where(j => work.Contains(j.WorkArrangement));
        if (Has(r.Clearance, out var clearance)) q = q.Where(j => clearance.Contains(j.Clearance));
        if (Has(r.Agency, out var agencies)) q = q.Where(j => agencies.Contains(j.Agency));
        if (Has(r.Seniority, out var seniority)) q = q.Where(j => j.Seniority.Any(s => seniority.Contains(s)));
        if (Has(r.State, out var states)) q = q.Where(j => j.States.Any(s => states.Contains(s)));
        if (Has(r.Specialty, out var specialties)) q = q.Where(j => j.Specialties.Any(s => specialties.Contains(s)) || j.SpecialtyCodes.Any(s => specialties.Contains(s)));
        if (Has(r.Series, out var series)) q = q.Where(j => j.Series.Any(s => series.Contains(s)));
        if (Has(r.Tech, out var tech))
        {
            foreach (var t in tech)
                q = q.Where(j => j.Tech.Contains(t));
        }

        if (r.MinSalary is { } min) q = q.Where(j => j.SalaryMax >= min);
        if (r.DirectHire is { } dh) q = q.Where(j => j.DirectHire == dh);
        if (r.OpenToPublic is { } pub) q = q.Where(j => j.OpenToPublic == pub);
        if (r.Supervisory is { } sup) q = q.Where(j => j.Supervisory == sup);
        if (r.MayCloseEarly is { } early) q = q.Where(j => j.MayCloseEarly == early);

        var now = time.GetUtcNow().UtcDateTime;
        if (r.PostedWithinDays is 1 or 3 or 7 or 14)
        {
            var since = now.AddDays(-r.PostedWithinDays.Value);
            q = q.Where(j => (j.OpenDate ?? j.FirstSeen) >= since);
        }
        if (r.ClosingWithinDays is 3 or 7)
        {
            var until = now.AddDays(r.ClosingWithinDays.Value);
            q = q.Where(j => j.CloseDate != null && j.CloseDate <= until);
        }

        if (Has(r.HiringPath, out var paths))
        {
            var codes = paths.SelectMany(HiringPath.CodesFor).Distinct().ToList();
            q = q.Where(j => j.HiringPaths.Any(p => codes.Contains(p)));
        }
        if (Has(r.Department, out var departments)) q = q.Where(j => departments.Contains(j.Department));
        if (Has(r.Schedule, out var schedules)) q = q.Where(j => j.ScheduleType != null && schedules.Contains(j.ScheduleType));
        if (Has(r.Appointment, out var appointments)) q = q.Where(j => j.AppointmentType != null && appointments.Contains(j.AppointmentType));
        if (Has(r.Travel, out var travel)) q = q.Where(j => j.TravelRequirement != null && travel.Contains(j.TravelRequirement));

        if (r.MinGrade is not null || r.MaxGrade is not null)
        {
            var minGrade = r.MinGrade ?? 1;
            var maxGrade = r.MaxGrade ?? 15;
            q = q.Where(j => j.PayPlan != null && GsPayPlans.Contains(j.PayPlan)
                             && (j.HighGrade ?? j.LowGrade) >= minGrade && j.LowGrade <= maxGrade);
        }
        if (r.MinPromotionPotential is { } potential) q = q.Where(j => j.PromotionPotential >= potential);

        if (Has(r.ExcludeAgency, out var notAgencies)) q = q.Where(j => !notAgencies.Contains(j.Agency));
        if (Has(r.ExcludeTech, out var notTech)) q = q.Where(j => !j.Tech.Any(t => notTech.Contains(t)));
        if (!string.IsNullOrWhiteSpace(r.ExcludeKeyword))
        {
            var words = r.ExcludeKeyword.Trim();
            q = q.Where(j => !EF.Functions.ToTsVector(FtsConfig, j.SearchText)
                .Matches(EF.Functions.WebSearchToTsQuery(FtsConfig, words)));
        }
        return q;
    }

    private static Expression<Func<Job, bool>> Located(LocationQuery loc, bool includeRemote)
    {
        var remote = includeRemote && loc.IsUs;

        if (loc.City is null)
        {
            var state = loc.State!;
            return j => j.States.Contains(state) || (remote && j.WorkArrangement == WorkArrangement.Remote);
        }

        // Locations read "City, State" (sometimes "Site, City, State"). The city must be a whole
        // segment, so "Houston" skips "Fort Sam Houston, Texas", and city and state must match
        // the same entry, so "Arlington, TX" skips Arlington, Virginia.
        var city = Escape(loc.City);
        var cityFirst = city + ",%";
        var cityInner = "%, " + city + ",%";
        var within = loc.State ?? loc.Region;
        var withinLast = within is null ? null : "%, " + Escape(within);
        return j => j.Locations.Any(l => (EF.Functions.ILike(l, cityFirst) || EF.Functions.ILike(l, cityInner))
                                         && (withinLast == null || EF.Functions.ILike(l, withinLast)))
                    || (remote && j.WorkArrangement == WorkArrangement.Remote);
    }

    /// <summary>Escapes the user's ILIKE wildcards (backslash is Postgres's default escape).</summary>
    private static string Escape(string value) =>
        value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    private static IQueryable<Job> Sort(IQueryable<Job> q, JobSearchRequest r)
    {
        var hasText = !string.IsNullOrWhiteSpace(r.Q);
        var sort = (r.Sort ?? (hasText ? "relevance" : "newest")).ToLowerInvariant();

        switch (sort)
        {
            case "relevance" when hasText:
                var text = r.Q!.Trim();
                return q.OrderByDescending(j => EF.Functions.ToTsVector(FtsConfig, j.SearchText)
                        .Rank(EF.Functions.WebSearchToTsQuery(FtsConfig, text)))
                    .ThenByDescending(j => j.OpenDate);
            case "closing":
                return q.OrderBy(j => j.CloseDate).ThenBy(j => j.Id);
            case "salary":
                return q.OrderByDescending(j => j.SalaryMax).ThenBy(j => j.Id);
            default:
                return q.OrderByDescending(j => j.OpenDate).ThenBy(j => j.Id);
        }
    }

    private static bool Has(string[]? values, out List<string> cleaned)
    {
        cleaned = values?.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct().ToList() ?? [];
        return cleaned.Count > 0;
    }

    private static IEnumerable<string> Flags(bool directHire, bool mayCloseEarly)
    {
        if (directHire) yield return "Direct hire";
        if (mayCloseEarly) yield return "May close early";
    }

    private static IReadOnlyList<FacetValue> Count(IEnumerable<string> values, int? top = null)
    {
        var counted = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .GroupBy(v => v)
            .Select(g => new FacetValue(g.Key, g.Count()))
            .OrderByDescending(f => f.Count)
            .ThenBy(f => f.Value);
        return (top is { } n ? counted.Take(n) : counted).ToList();
    }

    /// <summary>Ordered facets (seniority, clearance) read better in their natural order than by count.</summary>
    private static IReadOnlyList<FacetValue> InOrder(IReadOnlyList<FacetValue> values, IReadOnlyList<string> order)
    {
        var rank = order.Select((name, index) => (name, index)).ToDictionary(x => x.name, x => x.index);
        return values.OrderBy(v => rank.TryGetValue(v.Value, out var i) ? i : int.MaxValue).ThenBy(v => v.Value).ToList();
    }

    private static string Truncate(string s, int max)
    {
        if (s.Length <= max) return s;
        var cut = s.LastIndexOf(' ', max - 1);
        if (cut <= 0) cut = max;
        return s[..cut] + "...";
    }
}
