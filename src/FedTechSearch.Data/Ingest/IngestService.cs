using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FedTechSearch.Core.Enrichment;
using FedTechSearch.Core.Model;
using FedTechSearch.Data.UsaJobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FedTechSearch.Data.Ingest;

public sealed record IngestResult(int Seen, int Enriched, int Unchanged, int Deactivated);

/// <summary>
/// Pulls announcements, enriches new or changed ones, upserts them, and retires jobs that
/// closed or disappeared from USAJOBS.
/// </summary>
public sealed class IngestService
{
    private readonly AppDbContext _db;
    private readonly UsaJobsClient _client;
    private readonly JobEnricher _enricher;
    private readonly ILogger<IngestService> _logger;

    public IngestService(AppDbContext db, UsaJobsClient client, JobEnricher enricher, ILogger<IngestService> logger)
    {
        _db = db;
        _client = client;
        _enricher = enricher;
        _logger = logger;
    }

    public async Task<IngestResult> RunFromApiAsync(CancellationToken cancellationToken)
    {
        if (!_client.IsConfigured)
            throw new InvalidOperationException(
                "USAJOBS API key is not configured. Set UsaJobs__ApiKey and UsaJobs__UserAgentEmail " +
                "(request a free key at https://developer.usajobs.gov/apirequest/), or run with --fixture <file>.");

        var runStart = DateTime.UtcNow;
        var total = new Counter();

        await foreach (var page in _client.SearchAllPagesAsync(cancellationToken))
        {
            using (page)
            {
                await ProcessPageAsync(page.RootElement, runStart, total, cancellationToken);
            }
        }

        // Only retire missing jobs after a complete, successful pull.
        var deactivated = await _db.Jobs
            .Where(j => j.IsActive && (j.LastSeen < runStart || (j.CloseDate != null && j.CloseDate < runStart)))
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.IsActive, false), cancellationToken);

        var result = new IngestResult(total.Seen, total.Enriched, total.Unchanged, deactivated);
        _logger.LogInformation("Ingest finished: {Result}", result);
        return result;
    }

    /// <summary>Ingests a saved USAJOBS search response. Handy for demos and for working without an API key.</summary>
    public async Task<IngestResult> RunFromFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var total = new Counter();
        // Fixture postings are treated as open so a saved sample stays browsable after its close date.
        await ProcessPageAsync(doc.RootElement, DateTime.UtcNow, total, cancellationToken, forceActive: true);
        var result = new IngestResult(total.Seen, total.Enriched, total.Unchanged, 0);
        _logger.LogInformation("Fixture ingest finished: {Result}", result);
        return result;
    }

    private async Task ProcessPageAsync(JsonElement root, DateTime now, Counter total, CancellationToken cancellationToken, bool forceActive = false)
    {
        var raws = UsaJobsMapper.Items(root).Select(UsaJobsMapper.Map).OfType<RawJob>()
            .GroupBy(r => r.Id).Select(g => g.First())
            .ToList();
        if (raws.Count == 0) return;

        var ids = raws.Select(r => r.Id).ToList();
        var existing = await _db.Jobs.Where(j => ids.Contains(j.Id)).ToDictionaryAsync(j => j.Id, cancellationToken);

        foreach (var raw in raws)
        {
            total.Seen++;
            var hash = ContentHash(raw);

            if (!existing.TryGetValue(raw.Id, out var job))
            {
                job = new Job { Id = raw.Id, FirstSeen = now };
                _db.Jobs.Add(job);
            }

            var needsEnrichment = job.ContentHash != hash || job.EnrichmentVersion != _enricher.EnrichmentVersion;
            ApplyRaw(job, raw);
            job.LastSeen = now;
            job.IsActive = forceActive || raw.CloseDate is null || raw.CloseDate >= now;

            if (needsEnrichment)
            {
                var fields = await _enricher.EnrichAsync(raw, cancellationToken);
                ApplyEnrichment(job, fields);
                job.ContentHash = hash;
                job.EnrichmentVersion = _enricher.EnrichmentVersion;
                job.SearchText = BuildSearchText(job);
                total.Enriched++;
            }
            else
            {
                total.Unchanged++;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Saved page: {Count} jobs ({Seen} seen so far)", raws.Count, total.Seen);
    }

    public static void ApplyRaw(Job job, RawJob raw)
    {
        job.AnnouncementNumber = raw.AnnouncementNumber;
        job.Url = raw.Url;
        job.ApplyUrl = raw.ApplyUrl;
        job.OfficialTitle = raw.Title;
        job.Agency = raw.Agency;
        job.Department = raw.Department;
        job.SubAgency = raw.SubAgency;
        job.LocationDisplay = raw.LocationDisplay;
        job.Locations = raw.Locations;
        job.States = raw.States;
        job.Series = raw.Series;
        job.PayPlan = raw.PayPlan;
        job.LowGrade = raw.LowGrade;
        job.HighGrade = raw.HighGrade;
        job.SalaryMin = raw.SalaryMin;
        job.SalaryMax = raw.SalaryMax;
        job.SalaryInterval = raw.SalaryInterval;
        job.OpenDate = raw.OpenDate;
        job.CloseDate = raw.CloseDate;
        job.Schedule = raw.Schedule;
        job.OfferingType = raw.OfferingType;
        job.ScheduleType = raw.ScheduleType;
        job.AppointmentType = raw.AppointmentType;
        job.TravelRequirement = raw.TravelRequirement;
        job.PromotionPotential = raw.PromotionPotential;
        job.HiringPaths = raw.HiringPaths;
        job.Summary = raw.Summary;
        job.Duties = raw.Duties;
        job.Qualifications = raw.Qualifications;
    }

    public static void ApplyEnrichment(Job job, EnrichedFields f)
    {
        job.WorkingTitle = f.WorkingTitle;
        job.WorkingTitleSource = f.WorkingTitleSource;
        job.RoleFamily = f.RoleFamily;
        job.RoleConfidence = Math.Round(f.RoleConfidence, 3);
        job.SpecialtyCodes = f.SpecialtyCodes;
        job.Specialties = f.Specialties;
        job.Tech = f.Tech;
        job.Seniority = f.Seniority;
        job.Supervisory = f.Supervisory;
        job.WorkArrangement = f.WorkArrangement;
        job.Clearance = f.Clearance;
        job.DirectHire = f.DirectHire;
        job.OpenToPublic = f.OpenToPublic;
        job.MayCloseEarly = f.MayCloseEarly;
    }

    public static string BuildSearchText(Job job) => string.Join(" \n",
        new[] { job.OfficialTitle, job.WorkingTitle, job.RoleFamily, job.Agency, job.Department, job.SubAgency ?? "", job.Summary }
            .Concat(job.Specialties)
            .Concat(job.Tech)
            .Concat(job.Duties)
            .Append(job.Qualifications)
            .Where(s => !string.IsNullOrWhiteSpace(s)));

    /// <summary>Hash of the fields enrichment reads; unchanged postings skip re-enrichment (and LLM cost).</summary>
    public static string ContentHash(RawJob raw)
    {
        var material = string.Join("\u001f",
            raw.Title, raw.Summary, string.Join("\u001e", raw.Duties), raw.Qualifications, raw.Requirements, raw.HowToApply,
            raw.PayPlan, raw.LowGrade, raw.HighGrade, raw.SalaryMin, raw.SalaryMax, raw.SalaryInterval,
            raw.TeleworkEligible, raw.RemoteIndicator, raw.SecurityClearance, raw.LocationDisplay,
            string.Join("\u001e", raw.HiringPaths), raw.WhoMayApply, string.Join("\u001e", raw.Series));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    private sealed class Counter
    {
        public int Seen;
        public int Enriched;
        public int Unchanged;
    }
}
