using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FedTechSearch.Core.Enrichment;
using FedTechSearch.Core.Model;
using FedTechSearch.Data.Email;
using FedTechSearch.Data.Stats;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FedTechSearch.Data.Tests;

[Collection(PostgresCollection.Name)]
public sealed class StatsTests(PostgresFixture db)
{
    /// <summary>Noon Eastern on a day the fixture jobs are open (they open 2026-11-10).</summary>
    private static readonly DateTimeOffset Noon = new(2026, 11, 12, 17, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 12);

    private async Task<T> With<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = db.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    private Task With(Func<IServiceProvider, Task> work) => With<int>(async sp => { await work(sp); return 0; });

    private async Task Reset(DateTimeOffset now)
    {
        db.Clock.SetUtcNow(now);
        await With(sp => sp.GetRequiredService<AppDbContext>().DailyStats.ExecuteDeleteAsync());
    }

    private Task<int> Snapshot() => With(sp => sp.GetRequiredService<StatsSnapshotService>().TakeAsync(CancellationToken.None));

    private Task<List<DailyStat>> Rows() => With(sp => sp.GetRequiredService<AppDbContext>().DailyStats.AsNoTracking().ToListAsync());

    private Task<StatsResponse> Query(string dimension, int? days = null) =>
        With(sp => sp.GetRequiredService<StatsQuery>().GetAsync(dimension, days, 25, CancellationToken.None));

    [Fact]
    public async Task Snapshot_counts_match_the_jobs_table()
    {
        await Reset(Noon);
        await Snapshot();
        var rows = await Rows();

        var expected = await With(async sp =>
        {
            var jobs = sp.GetRequiredService<AppDbContext>().Jobs.Where(j => j.IsActive);
            var tech = await jobs.SelectMany(j => j.Tech).GroupBy(t => t).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstAsync();
            return new
            {
                Active = await jobs.CountAsync(),
                Remote = await jobs.CountAsync(j => j.WorkArrangement == WorkArrangement.Remote),
                Telework = await jobs.CountAsync(j => j.WorkArrangement == WorkArrangement.Telework),
                New = await jobs.CountAsync(j => j.OpenDate >= new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc)
                                                 && j.OpenDate < new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc)),
                Tech = tech,
                TechCount = await jobs.CountAsync(j => j.Tech.Contains(tech))
            };
        });

        Assert.All(rows, r => Assert.Equal(Today, r.Date));
        var total = Assert.Single(rows, r => r.Dimension == StatsDimensions.Total);
        Assert.Equal(StatsDimensions.TotalValue, total.Value);
        Assert.Equal((expected.Active, expected.Remote, expected.Telework, expected.New),
            (total.ActiveCount, total.RemoteCount, total.TeleworkCount, total.NewCount));
        Assert.True(expected.New > 0);
        Assert.Equal(expected.TechCount, rows.Single(r => r.Dimension == "tech" && r.Value == expected.Tech).ActiveCount);
        Assert.Equal(expected.Active, rows.Where(r => r.Dimension == "roleFamily").Sum(r => r.ActiveCount)); // one family per job
        Assert.All(StatsDimensions.All, d => Assert.Contains(rows, r => r.Dimension == d));
    }

    [Fact]
    public async Task The_date_is_the_eastern_date()
    {
        await Reset(new DateTimeOffset(2026, 11, 13, 2, 0, 0, TimeSpan.Zero)); // 9 PM Eastern on the 12th
        await Snapshot();
        Assert.All(await Rows(), r => Assert.Equal(Today, r.Date));
    }

    [Fact]
    public async Task Taking_the_snapshot_twice_a_day_replaces_that_day_only()
    {
        await Reset(Noon.AddDays(-1));
        await Snapshot();
        var yesterday = (await Rows()).Count;

        db.Clock.SetUtcNow(Noon);
        var first = await Snapshot();
        // A value that is gone by the next run disappears from today's rows.
        await With(async sp =>
        {
            var context = sp.GetRequiredService<AppDbContext>();
            context.DailyStats.Add(new DailyStat { Date = Today, Dimension = "tech", Value = "Gone by evening", ActiveCount = 1 });
            await context.SaveChangesAsync();
        });
        db.Clock.SetUtcNow(Noon.AddHours(6));
        var second = await Snapshot();

        var rows = await Rows();
        Assert.Equal(first, second);
        Assert.Equal(second, rows.Count(r => r.Date == Today));
        Assert.Equal(yesterday, rows.Count(r => r.Date == Today.AddDays(-1)));
        Assert.DoesNotContain(rows, r => r.Value == "Gone by evening");
    }

    [Fact]
    public async Task Query_returns_top_values_with_series_and_a_week_over_week_change()
    {
        await Reset(Noon);
        await Snapshot();

        var withoutHistory = await Query("tech");
        Assert.Equal(Today, withoutHistory.Date);
        Assert.True(withoutHistory.Top.Count > 1);
        Assert.Equal(withoutHistory.Top.OrderByDescending(v => v.Active).Select(v => v.Active), withoutHistory.Top.Select(v => v.Active));
        Assert.All(withoutHistory.Top, v => Assert.Null(v.Change7d)); // no snapshot a week ago
        Assert.All(withoutHistory.Series, s => Assert.Single(s.Points));
        Assert.True(withoutHistory.Total.Active > 0);

        // A week-old snapshot holding only the leading value, three jobs lower.
        var leader = withoutHistory.Top[0];
        await With(async sp =>
        {
            var context = sp.GetRequiredService<AppDbContext>();
            context.DailyStats.Add(new DailyStat { Date = Today.AddDays(-7), Dimension = "tech", Value = leader.Value, ActiveCount = leader.Active - 3 });
            await context.SaveChangesAsync();
        });

        var withHistory = await Query("tech");
        Assert.Equal(3, withHistory.Top[0].Change7d);
        Assert.Equal(withHistory.Top[1].Active, withHistory.Top[1].Change7d); // missing a week ago = had none
        Assert.Equal([Today.AddDays(-7), Today], withHistory.Series[0].Points.Select(p => p.Date));
        Assert.Single((await Query("tech", days: 3)).Series[0].Points); // the series honors days; the change doesn't need it
        Assert.Equal(3, (await Query("tech", days: 3)).Top[0].Change7d);
    }

    [Fact]
    public async Task Query_before_any_snapshot_is_empty()
    {
        await Reset(Noon);
        var empty = await Query("agency");
        Assert.Null(empty.Date);
        Assert.Empty(empty.Top);
        Assert.Equal(0, empty.Total.Active);
    }

    [Fact]
    public async Task Stats_endpoint_returns_the_shape_and_rejects_unknown_dimensions()
    {
        await Reset(Noon);
        await Snapshot();
        Environment.SetEnvironmentVariable("ConnectionStrings__Jobs", db.ConnectionString);
        using var api = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .ConfigureServices(s => s.AddSingleton<IEmailSender>(db.Emails)));
        var client = api.CreateClient();

        var response = await client.GetAsync("/api/stats?dimension=roleFamily&days=30&top=5");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("roleFamily", body.GetProperty("dimension").GetString());
        Assert.Equal("2026-11-12", body.GetProperty("date").GetString());
        Assert.Equal(30, body.GetProperty("days").GetInt32());
        Assert.True(body.GetProperty("total").GetProperty("active").GetInt32() > 0);
        Assert.InRange(body.GetProperty("top").GetArrayLength(), 1, 5);
        Assert.Equal(body.GetProperty("top").GetArrayLength(), body.GetProperty("series").GetArrayLength());

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/stats?dimension=salary")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/stats")).StatusCode);
    }
}
