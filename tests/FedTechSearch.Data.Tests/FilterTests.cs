using FedTechSearch.Core.Enrichment;
using FedTechSearch.Data.Search;
using Microsoft.Extensions.DependencyInjection;

namespace FedTechSearch.Data.Tests;

[Collection(PostgresCollection.Name)]
public sealed class FilterTests(PostgresFixture db)
{
    private async Task<JobSearchResponse> Search(JobSearchRequest request)
    {
        request.PageSize ??= 100;
        using var scope = db.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<JobSearch>().SearchAsync(request, CancellationToken.None);
    }

    private async Task<int> Total(JobSearchRequest request) => (await Search(request)).Total;

    private void Now(int month, int day) => db.Clock.SetUtcNow(new DateTimeOffset(2026, month, day, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Posted_within_counts_back_from_now()
    {
        Now(11, 20);
        Assert.Equal(7, await Total(new() { PostedWithinDays = 14 }));
        Assert.Equal(0, await Total(new() { PostedWithinDays = 7 }));
        Assert.Equal(7, await Total(new() { PostedWithinDays = 5 })); // not an allowed window: ignored
    }

    [Fact]
    public async Task Closing_within_counts_forward_from_now()
    {
        Now(11, 20);
        Assert.Equal(7, await Total(new() { ClosingWithinDays = 7 }));
        Assert.Equal(0, await Total(new() { ClosingWithinDays = 3 }));
    }

    [Fact]
    public async Task Hiring_path_filters_by_plain_label()
    {
        Assert.Equal(2, await Total(new() { HiringPath = ["Veterans"] }));
        Assert.Equal(3, await Total(new() { HiringPath = [HiringPath.FederalEmployees] }));

        var facet = (await Search(new())).Facets["hiringPath"];
        Assert.Equal("Open to the public", facet[0].Value);
        Assert.Equal(3, facet.Single(f => f.Value == HiringPath.FederalEmployees).Count);
    }

    [Fact]
    public async Task Grade_range_keeps_overlapping_gs_jobs_only()
    {
        Assert.Equal(5, await Total(new() { MinGrade = 13 }));               // drops GS-7/9 and the ZP job
        Assert.Equal(2, await Total(new() { MaxGrade = 12 }));               // GS-12/13 ladder and GS-7/9
        Assert.Equal(3, await Total(new() { MinPromotionPotential = 14 }));
    }

    [Fact]
    public async Task Exclusions_remove_matching_jobs()
    {
        Assert.Equal(6, await Total(new() { ExcludeAgency = ["Social Security Administration"] }));

        var tech = (await Search(new())).Facets["tech"][0];
        Assert.Equal(7 - tech.Count, await Total(new() { ExcludeTech = [tech.Value] }));

        var withWord = await Total(new() { Q = "kubernetes" });
        Assert.True(withWord > 0);
        Assert.Equal(7 - withWord, await Total(new() { ExcludeKeyword = "kubernetes" }));
    }

    [Fact]
    public async Task Department_schedule_travel_and_may_close_early()
    {
        Assert.Equal(1, await Total(new() { Department = ["Department of Defense"] }));
        Assert.Equal(7, await Total(new() { Schedule = [ScheduleType.FullTime] }));
        Assert.Equal(1, await Total(new() { Travel = [TravelRequirement.UpTo25] }));

        var early = await Search(new() { MayCloseEarly = true });
        Assert.Equal("Social Security Administration", Assert.Single(early.Items).Agency);
        Assert.Equal(1, early.Facets["flags"].Single(f => f.Value == "May close early").Count);
    }

    [Fact]
    public async Task Role_type_facet_splits_individual_contributors_and_managers()
    {
        var facet = (await Search(new())).Facets["roleType"];
        Assert.Equal(7, facet.Sum(f => f.Count));
        Assert.Equal(JobSearch.IndividualContributor, facet[0].Value);
    }
}
