using FedTechSearch.Data.Ingest;
using FedTechSearch.Data.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FedTechSearch.Data.Tests;

/// <summary>Fixture: one job in downtown Houston, one at Ellington Field (~16 mi away), one remote job.</summary>
[Collection(PostgresCollection.Name)]
public sealed class RadiusTests(PostgresFixture db)
{
    private async Task<JobSearchResponse> Search(JobSearchRequest request)
    {
        request.PageSize ??= 100;
        request.IncludeRemote ??= false;
        using var scope = db.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<JobSearch>().SearchAsync(request, CancellationToken.None);
    }

    private static List<string> Places(JobSearchResponse r) => r.Items.Select(i => i.LocationDisplay).Order().ToList();

    [Fact]
    public async Task Radius_grows_to_include_nearby_duty_stations()
    {
        Assert.Equal(["Houston, Texas"], Places(await Search(new() { Location = "Houston, TX", Radius = 10 })));
        Assert.Equal(["Ellington Field, Texas", "Houston, Texas"], Places(await Search(new() { Location = "Houston, TX", Radius = 25 })));
        Assert.Equal(["Houston, Texas"], Places(await Search(new() { Location = "Houston, TX", Radius = 0 })));
    }

    [Fact]
    public async Task City_search_defaults_to_25_miles_and_reports_the_place()
    {
        var result = await Search(new() { Location = "houston, tx, us" });

        Assert.Equal(2, result.Total);
        Assert.Equal(new LocationInfo("radius", "Houston, Texas", 25), result.Location);
    }

    [Fact]
    public async Task Nearest_sort_orders_by_distance_and_cards_get_miles()
    {
        var result = await Search(new() { Location = "Houston, TX", Radius = 25, Sort = "nearest", IncludeRemote = true });

        Assert.Equal(["Houston, Texas", "Ellington Field, Texas"], result.Items.Take(2).Select(i => i.LocationDisplay));
        Assert.Equal(0, result.Items[0].DistanceMiles!.Value, 0.5);
        Assert.InRange(result.Items[1].DistanceMiles!.Value, 14, 18);
        Assert.Equal("Remote", result.Items[2].WorkArrangement); // no in-radius location: sorted last
        Assert.Null(result.Items[2].DistanceMiles);
    }

    [Fact]
    public async Task Unknown_cities_fall_back_to_text_and_states_stay_states()
    {
        var unknown = await Search(new() { Location = "Smallville, KS" });
        Assert.Equal("text", unknown.Location!.Mode);
        Assert.Equal(0, unknown.Total);

        var state = await Search(new() { Location = "TX" });
        Assert.Equal("state", state.Location!.Mode);
        Assert.Equal(2, state.Total);
    }

    [Fact]
    public async Task Reingesting_keeps_location_rows_and_skips_unchanged_jobs()
    {
        using var scope = db.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = await context.JobLocations.CountAsync();
        var ids = await context.JobLocations.Select(l => l.Id).OrderBy(id => id).ToListAsync();

        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "sample-search.json");
        await scope.ServiceProvider.GetRequiredService<IngestService>().RunFromFileAsync(path, CancellationToken.None);

        Assert.Equal(before, await context.JobLocations.CountAsync());
        Assert.Equal(ids, await context.JobLocations.Select(l => l.Id).OrderBy(id => id).ToListAsync()); // not rewritten
        Assert.True(before >= 9);
    }
}
