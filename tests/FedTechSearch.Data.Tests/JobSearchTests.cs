using FedTechSearch.Data.Search;
using Microsoft.Extensions.DependencyInjection;

namespace FedTechSearch.Data.Tests;

[Collection(PostgresCollection.Name)]
public sealed class JobSearchTests(PostgresFixture db)
{
    private async Task<JobSearchResponse> Search(JobSearchRequest request)
    {
        using var scope = db.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<JobSearch>().SearchAsync(request, CancellationToken.None);
    }

    [Fact]
    public async Task Unfiltered_search_returns_every_fixture_job_with_facets()
    {
        var result = await Search(new JobSearchRequest { PageSize = 100 });

        Assert.Equal(9, result.Total);
        Assert.NotEmpty(result.Facets["roleFamily"]);
        Assert.NotEmpty(result.Facets["tech"]);
    }

    [Fact]
    public async Task Location_matches_a_city_inside_a_multi_location_posting()
    {
        var result = await Search(new JobSearchRequest { Location = "Baltimore, MD", IncludeRemote = false, Radius = 0 });

        var job = Assert.Single(result.Items);
        Assert.Equal("Multiple Locations", job.LocationDisplay);
    }

    [Fact]
    public async Task Location_includes_remote_jobs_by_default()
    {
        var result = await Search(new JobSearchRequest { Location = "Houston, TX", Radius = 0 });

        Assert.Equal(2, result.Total);
        Assert.Contains(result.Items, j => j.WorkArrangement == "Remote");
        Assert.Contains(result.Items, j => j.LocationDisplay == "Houston, Texas");
    }
}
