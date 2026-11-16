using FedTechSearch.Core.Feedback;
using FedTechSearch.Core.Model;
using FedTechSearch.Data.Feedback;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FedTechSearch.Data.Tests;

[Collection(PostgresCollection.Name)]
public sealed class FeedbackTests(PostgresFixture db)
{
    private async Task<T> With<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = db.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    /// <summary>A fixture job with at least one tech tag, and a clean feedback table.</summary>
    private Task<Job> FreshJob() => With(async sp =>
    {
        var context = sp.GetRequiredService<AppDbContext>();
        await context.TagFeedback.ExecuteDeleteAsync();
        return await context.Jobs.AsNoTracking().FirstAsync(j => j.Tech.Count > 0);
    });

    [Fact]
    public async Task Submit_stores_the_report_with_a_hash_instead_of_the_ip()
    {
        var job = await FreshJob();
        var result = await With(sp => sp.GetRequiredService<FeedbackService>()
            .SubmitAsync(job.Id, new FeedbackInput("tech", job.Tech[0], null, "Not in the duties"), "203.0.113.7", CancellationToken.None));

        Assert.Equal(SubmitOutcome.Stored, result.Outcome);
        var row = await With(sp => sp.GetRequiredService<AppDbContext>().TagFeedback.AsNoTracking().SingleAsync());
        Assert.Equal(job.Tech[0], row.CurrentValue);
        Assert.True(row.TaxonomyVersion > 0);
        Assert.Equal(64, row.ClientHash.Length);
        Assert.DoesNotContain("203.0.113.7", row.ClientHash);
    }

    [Fact]
    public async Task Same_client_same_day_same_report_is_stored_once_and_unknown_jobs_are_reported()
    {
        var job = await FreshJob();
        var input = new FeedbackInput("tech", job.Tech[0], null, null);
        for (var i = 0; i < 3; i++)
            await With(sp => sp.GetRequiredService<FeedbackService>().SubmitAsync(job.Id, input, "203.0.113.8", CancellationToken.None));
        await With(sp => sp.GetRequiredService<FeedbackService>().SubmitAsync(job.Id, input, "203.0.113.9", CancellationToken.None));

        Assert.Equal(2, await With(sp => sp.GetRequiredService<AppDbContext>().TagFeedback.CountAsync()));
        var missing = await With(sp => sp.GetRequiredService<FeedbackService>().SubmitAsync("nope", input, "203.0.113.8", CancellationToken.None));
        Assert.Equal(SubmitOutcome.JobNotFound, missing.Outcome);
    }

    [Fact]
    public async Task Summary_groups_counts_and_windows_by_date()
    {
        var job = await FreshJob();
        var now = db.Clock.GetUtcNow().UtcDateTime;
        var tech = job.Tech[0];
        await With(async sp =>
        {
            var context = sp.GetRequiredService<AppDbContext>();
            TagFeedback Row(string? suggested, int daysAgo, string note) => new()
            {
                JobId = job.Id, Field = "tech", CurrentValue = tech, SuggestedValue = suggested, Note = note,
                CreatedAt = now.AddDays(-daysAgo), TaxonomyVersion = 1, ClientHash = Guid.NewGuid().ToString("N")
            };
            context.TagFeedback.AddRange(Row(null, 1, "newest"), Row(null, 3, "older"), Row(null, 5, "oldest"),
                Row("Kubernetes", 2, "suggest"), Row(null, 200, "outside the window"));
            return await context.SaveChangesAsync();
        });

        var summary = await With(sp => sp.GetRequiredService<FeedbackService>().SummaryAsync(90, 2, CancellationToken.None));

        Assert.Equal(4, summary.Total);
        var top = summary.Groups[0];
        Assert.Equal(("tech", tech, (string?)null, 3), (top.Field, top.CurrentValue, top.SuggestedValue, top.Count));
        Assert.Equal(["newest", "older"], top.Examples.Select(e => e.Note));
        Assert.Equal("Kubernetes", summary.Groups[1].SuggestedValue);
        Assert.Equal(1, summary.Groups[1].Count);
    }
}
