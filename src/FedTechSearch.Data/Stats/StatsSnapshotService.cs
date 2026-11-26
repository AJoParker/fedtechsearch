using FedTechSearch.Core.Enrichment;
using FedTechSearch.Core.Model;
using FedTechSearch.Data.Alerts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FedTechSearch.Data.Stats;

public static class StatsDimensions
{
    public const string Total = "total";
    public const string TotalValue = "all";

    /// <summary>Dimensions the snapshot writes and the stats endpoint accepts.</summary>
    public static readonly IReadOnlyList<string> All =
        ["tech", "roleFamily", "agency", "state", "seniority", "clearance", "work", Total];
}

/// <summary>
/// Records today's counts in <c>daily_stats</c> after each ingest. Today's rows are replaced as a
/// whole, so running it several times a day is safe and values that vanished during the day go
/// away too. "Today" is the local date in the alerts time zone (Eastern by default).
/// </summary>
public sealed class StatsSnapshotService(AppDbContext db, TimeProvider time, IOptions<AlertOptions> alertOptions, ILogger<StatsSnapshotService> logger)
{
    private const int MaxValueLength = 200;
    private const int NewWindowDays = 7;

    public async Task<int> TakeAsync(CancellationToken cancellationToken)
    {
        var zone = AlertSchedule.Zone(alertOptions.Value.TimeZone);
        var nowUtc = time.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone));
        // OpenDate is a calendar date from USAJOBS, so compare dates, not instants.
        var newFrom = today.AddDays(-(NewWindowDays - 1)).ToDateTime(TimeOnly.MinValue);
        var newTo = today.ToDateTime(TimeOnly.MaxValue);

        var jobs = await db.Jobs.AsNoTracking().Where(j => j.IsActive).Select(j => new ActiveJob(
            j.RoleFamily, j.Tech, j.Agency, j.States, j.Seniority, j.Clearance, j.WorkArrangement, j.OpenDate))
            .ToListAsync(cancellationToken);

        var rows = new List<DailyStat>();
        void Add(string dimension, Func<ActiveJob, IEnumerable<string>> valuesOf)
        {
            var counts = new Dictionary<string, DailyStat>(StringComparer.Ordinal);
            foreach (var job in jobs)
            {
                var isNew = job.OpenDate >= newFrom && job.OpenDate <= newTo;
                var isRemote = job.WorkArrangement == WorkArrangement.Remote;
                var isTelework = job.WorkArrangement == WorkArrangement.Telework;
                foreach (var value in valuesOf(job).Distinct())
                {
                    if (string.IsNullOrWhiteSpace(value) || value.Length > MaxValueLength) continue;
                    if (!counts.TryGetValue(value, out var row))
                        counts[value] = row = new DailyStat { Date = today, Dimension = dimension, Value = value };
                    row.ActiveCount++;
                    if (isNew) row.NewCount++;
                    if (isRemote) row.RemoteCount++;
                    if (isTelework) row.TeleworkCount++;
                }
            }
            rows.AddRange(counts.Values);
        }

        Add("tech", j => j.Tech);
        Add("roleFamily", j => [j.RoleFamily]);
        Add("agency", j => [j.Agency]);
        Add("state", j => j.States);
        Add("seniority", j => j.Seniority);
        Add("clearance", j => [j.Clearance]);
        Add("work", j => [j.WorkArrangement]);
        Add(StatsDimensions.Total, _ => [StatsDimensions.TotalValue]);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.DailyStats.Where(s => s.Date == today).ExecuteDeleteAsync(cancellationToken);
        db.DailyStats.AddRange(rows);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        db.ChangeTracker.Clear();

        logger.LogInformation("Stats snapshot for {Date}: {Rows} rows from {Jobs} active jobs", today, rows.Count, jobs.Count);
        return rows.Count;
    }

    private sealed record ActiveJob(
        string RoleFamily, List<string> Tech, string Agency, List<string> States, List<string> Seniority,
        string Clearance, string WorkArrangement, DateTime? OpenDate);
}
