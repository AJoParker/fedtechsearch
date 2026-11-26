using System.Text.Json;
using FedTechSearch.Core.Model;
using FedTechSearch.Core.Qualifications;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FedTechSearch.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobLocation> JobLocations => Set<JobLocation>();
    public DbSet<TagFeedback> TagFeedback => Set<TagFeedback>();
    public DbSet<Subscriber> Subscribers => Set<Subscriber>();
    public DbSet<SavedSearch> SavedSearches => Set<SavedSearch>();
    public DbSet<AuthToken> AuthTokens => Set<AuthToken>();
    public DbSet<AlertDelivery> AlertDeliveries => Set<AlertDelivery>();
    public DbSet<DailyStat> DailyStats => Set<DailyStat>();

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
        // Stored as jsonb; read back whole, so a plain JSON conversion is enough.
        job.Property(j => j.QualificationInfo)
            .HasColumnType("jsonb")
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, JsonOptions),
                v => v == null ? null : JsonSerializer.Deserialize<QualificationInfo>(v, JsonOptions),
                new ValueComparer<QualificationInfo?>(
                    (a, b) => JsonSerializer.Serialize(a, JsonOptions) == JsonSerializer.Serialize(b, JsonOptions),
                    v => JsonSerializer.Serialize(v, JsonOptions).GetHashCode(),
                    v => v == null ? null : JsonSerializer.Deserialize<QualificationInfo>(JsonSerializer.Serialize(v, JsonOptions), JsonOptions)));

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

        var subscriber = modelBuilder.Entity<Subscriber>();
        subscriber.ToTable("subscribers");
        subscriber.Property(x => x.Id).ValueGeneratedNever(); // Guids are created in code
        subscriber.Property(x => x.Email).HasMaxLength(254);
        subscriber.HasIndex(x => x.Email).IsUnique();
        subscriber.HasMany(x => x.Searches).WithOne().HasForeignKey(x => x.SubscriberId).OnDelete(DeleteBehavior.Cascade);

        var search = modelBuilder.Entity<SavedSearch>();
        search.ToTable("saved_searches");
        search.Property(x => x.Id).ValueGeneratedNever();
        search.Property(x => x.Name).HasMaxLength(80);
        search.Property(x => x.QueryString).HasMaxLength(2000);
        search.Property(x => x.Frequency).HasConversion<string>().HasMaxLength(16);

        var token = modelBuilder.Entity<AuthToken>();
        token.ToTable("auth_tokens");
        token.Property(x => x.TokenHash).HasMaxLength(64);
        token.Property(x => x.Purpose).HasConversion<string>().HasMaxLength(16);
        token.HasIndex(x => x.TokenHash).IsUnique();
        token.HasIndex(x => new { x.SubscriberId, x.Purpose, x.CreatedAt });
        token.HasOne<Subscriber>().WithMany().HasForeignKey(x => x.SubscriberId).OnDelete(DeleteBehavior.Cascade);

        var delivery = modelBuilder.Entity<AlertDelivery>();
        delivery.ToTable("alert_deliveries");
        delivery.HasIndex(x => new { x.SavedSearchId, x.WindowStart }).IsUnique();
        delivery.HasOne<SavedSearch>().WithMany().HasForeignKey(x => x.SavedSearchId).OnDelete(DeleteBehavior.Cascade);

        var stat = modelBuilder.Entity<DailyStat>();
        stat.ToTable("daily_stats");
        stat.HasKey(x => new { x.Date, x.Dimension, x.Value });
        stat.Property(x => x.Dimension).HasMaxLength(32);
        stat.Property(x => x.Value).HasMaxLength(200);
        stat.HasIndex(x => new { x.Dimension, x.Date });
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
