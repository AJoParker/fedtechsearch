using FedTechSearch.Core.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FedTechSearch.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobLocation> JobLocations => Set<JobLocation>();
    public DbSet<TagFeedback> TagFeedback => Set<TagFeedback>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var job = modelBuilder.Entity<Job>();
        job.ToTable("jobs");
        job.HasKey(j => j.Id);
        job.Property(j => j.Id).HasMaxLength(64);
        job.Property(j => j.SalaryMin).HasPrecision(12, 2);
        job.Property(j => j.SalaryMax).HasPrecision(12, 2);

        job.HasIndex(j => j.IsActive);
        job.HasIndex(j => j.RoleFamily);
        job.HasIndex(j => j.CloseDate);
        job.HasIndex(j => j.OpenDate);
        job.HasIndex(j => j.Tech).HasMethod("gin");
        job.HasIndex(j => j.Seniority).HasMethod("gin");
        job.HasIndex(j => j.States).HasMethod("gin");
        job.HasIndex(j => j.SpecialtyCodes).HasMethod("gin");
        job.HasIndex(j => j.HiringPaths).HasMethod("gin");
        job.HasIndex(j => j.Department);
        job.HasIndex(j => j.PromotionPotential);
        job.HasMany(j => j.GeoLocations).WithOne().HasForeignKey(l => l.JobId).OnDelete(DeleteBehavior.Cascade);

        var location = modelBuilder.Entity<JobLocation>();
        location.ToTable("job_locations");
        location.Property(l => l.JobId).HasMaxLength(64);
        location.HasIndex(l => new { l.Latitude, l.Longitude });
        location.HasIndex(l => new { l.State, l.City });

        var feedback = modelBuilder.Entity<TagFeedback>();
        feedback.ToTable("tag_feedback");
        feedback.Property(f => f.JobId).HasMaxLength(64);
        feedback.Property(f => f.Field).HasMaxLength(32);
        feedback.Property(f => f.CurrentValue).HasMaxLength(100);
        feedback.Property(f => f.SuggestedValue).HasMaxLength(100);
        feedback.Property(f => f.Note).HasMaxLength(500);
        feedback.Property(f => f.ClientHash).HasMaxLength(64);
        feedback.HasOne<Job>().WithMany().HasForeignKey(f => f.JobId).OnDelete(DeleteBehavior.Cascade);
        feedback.HasIndex(f => new { f.Field, f.CurrentValue });
        feedback.HasIndex(f => f.CreatedAt);
    }
}

public static class DbInitializer
{
    /// <summary>
    /// Applies pending migrations. Only the ingest worker calls this, so two processes never
    /// race to change the schema. Retries while Postgres starts up.
    /// </summary>
    public static Task MigrateAsync(AppDbContext db, ILogger logger, CancellationToken cancellationToken = default) =>
        RetryAsync(logger, async () =>
        {
            await BaselineEnsureCreatedSchemaAsync(db, logger, cancellationToken);
            await db.Database.MigrateAsync(cancellationToken);
            return true;
        }, cancellationToken);

    /// <summary>Waits until the ingest worker has applied every migration. Used by the API.</summary>
    public static Task WaitForSchemaAsync(AppDbContext db, ILogger logger, CancellationToken cancellationToken = default) =>
        RetryAsync(logger, async () =>
        {
            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            if (pending.Count == 0) return true;
            logger.LogInformation("Waiting for migrations to be applied: {Pending}", string.Join(", ", pending));
            return false;
        }, cancellationToken);

    /// <summary>
    /// Databases created before migrations existed (by EnsureCreated) already match
    /// InitialCreate. Record it as applied so MigrateAsync doesn't try to create the table again.
    /// </summary>
    private static async Task BaselineEnsureCreatedSchemaAsync(AppDbContext db, ILogger logger, CancellationToken cancellationToken)
    {
        var state = await db.Database
            .SqlQueryRaw<bool>("""SELECT to_regclass('public.jobs') IS NOT NULL AND to_regclass('public."__EFMigrationsHistory"') IS NULL AS "Value" """)
            .SingleAsync(cancellationToken);
        if (!state) return;

        var history = db.GetService<IHistoryRepository>();
        var initial = db.Database.GetMigrations().First();
        await db.Database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript(), cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            history.GetInsertScript(new HistoryRow(initial, ProductInfo.GetVersion())), cancellationToken);
        logger.LogInformation("Existing schema baselined as {Migration}", initial);
    }

    private static async Task RetryAsync(ILogger logger, Func<Task<bool>> attemptOnce, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (await attemptOnce()) return;
            }
            catch (Exception ex) when (attempt < 40 && ex is NpgsqlException or TimeoutException or InvalidOperationException)
            {
                logger.LogWarning("Database not ready ({Message}); retry {Attempt}/40", ex.Message, attempt);
            }

            if (attempt >= 40) throw new TimeoutException("Database schema was not ready after 40 attempts.");
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        }
    }
}
