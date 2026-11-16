using System.Security.Cryptography;
using System.Text;
using FedTechSearch.Core.Feedback;
using FedTechSearch.Core.Model;
using FedTechSearch.Core.Taxonomy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FedTechSearch.Data.Feedback;

public sealed class FeedbackOptions
{
    public const string Section = "Feedback";

    /// <summary>
    /// Secret for hashing client IPs. When empty, a random key is made at startup, so hashes
    /// can't be linked across restarts either. Set it to keep same-day duplicate checks across restarts.
    /// </summary>
    public string? Salt { get; set; }
}

public enum SubmitOutcome { Stored, Duplicate, JobNotFound, Invalid }

public sealed record SubmitResult(SubmitOutcome Outcome, string? Error = null);

public sealed record FeedbackExample(string JobId, string WorkingTitle, string OfficialTitle, string Url, string? Note, DateTime CreatedAt);

public sealed record FeedbackGroup(
    string Field, string? CurrentValue, string? SuggestedValue, int Count, DateTime LastAt, IReadOnlyList<FeedbackExample> Examples);

public sealed record FeedbackSummary(int Days, int Total, IReadOnlyList<FeedbackGroup> Groups);

/// <summary>Stores "wrong tag" reports and builds the report used to fix taxonomy patterns.</summary>
public sealed class FeedbackService(AppDbContext db, CompiledTaxonomy taxonomy, TimeProvider time, FeedbackKey key)
{
    public async Task<SubmitResult> SubmitAsync(string jobId, FeedbackInput input, string? clientIp, CancellationToken cancellationToken)
    {
        var job = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (job is null) return new SubmitResult(SubmitOutcome.JobNotFound);

        var result = TagFeedbackValidator.Validate(job, input, taxonomy);
        if (!result.Ok) return new SubmitResult(SubmitOutcome.Invalid, result.Error);
        var f = result.Feedback!;

        var now = time.GetUtcNow().UtcDateTime;
        var clientHash = key.Hash(now, clientIp ?? "unknown");

        // One report per client, job, field and value per day; repeats are accepted but not stored.
        var dayStart = now.Date;
        var duplicate = await db.TagFeedback.AnyAsync(x =>
            x.ClientHash == clientHash && x.JobId == jobId && x.Field == f.Field
            && x.CurrentValue == f.CurrentValue && x.SuggestedValue == f.SuggestedValue && x.CreatedAt >= dayStart, cancellationToken);
        if (duplicate) return new SubmitResult(SubmitOutcome.Duplicate);

        db.TagFeedback.Add(new TagFeedback
        {
            JobId = jobId, Field = f.Field, CurrentValue = f.CurrentValue, SuggestedValue = f.SuggestedValue,
            Note = f.Note, CreatedAt = now, TaxonomyVersion = taxonomy.Version, ClientHash = clientHash
        });
        await db.SaveChangesAsync(cancellationToken);
        return new SubmitResult(SubmitOutcome.Stored);
    }

    /// <summary>Reports from the last <paramref name="days"/> days, grouped by field + wrong value + suggestion, most reported first.</summary>
    public async Task<FeedbackSummary> SummaryAsync(int days, int examplesPerGroup, CancellationToken cancellationToken)
    {
        days = Math.Clamp(days, 1, 3650);
        var since = time.GetUtcNow().UtcDateTime.AddDays(-days);

        // Feedback volume is small; group in memory over a narrow projection.
        var rows = await db.TagFeedback.AsNoTracking()
            .Where(f => f.CreatedAt >= since)
            .Join(db.Jobs, f => f.JobId, j => j.Id, (f, j) => new
            {
                f.Field, f.CurrentValue, f.SuggestedValue, f.Note, f.CreatedAt,
                f.JobId, j.WorkingTitle, j.OfficialTitle, j.Url
            })
            .ToListAsync(cancellationToken);

        var groups = rows
            .GroupBy(r => (r.Field, r.CurrentValue, r.SuggestedValue))
            .Select(g => new FeedbackGroup(
                g.Key.Field, g.Key.CurrentValue, g.Key.SuggestedValue, g.Count(), g.Max(r => r.CreatedAt),
                g.OrderByDescending(r => r.CreatedAt).Take(examplesPerGroup)
                    .Select(r => new FeedbackExample(r.JobId, r.WorkingTitle, r.OfficialTitle, r.Url, r.Note, r.CreatedAt))
                    .ToList()))
            .OrderByDescending(g => g.Count).ThenByDescending(g => g.LastAt)
            .ToList();

        return new FeedbackSummary(days, rows.Count, groups);
    }
}

/// <summary>Keyed hash of client IP + UTC day. The day in the input makes hashes unlinkable across days.</summary>
public sealed class FeedbackKey(IOptions<FeedbackOptions> options)
{
    private readonly byte[] _key = string.IsNullOrWhiteSpace(options.Value.Salt)
        ? RandomNumberGenerator.GetBytes(32)
        : Encoding.UTF8.GetBytes(options.Value.Salt);

    public string Hash(DateTime utcNow, string clientIp) =>
        Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{utcNow:yyyy-MM-dd}|{clientIp}")));
}
