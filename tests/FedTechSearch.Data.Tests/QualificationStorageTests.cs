using System.Net.Http.Json;
using System.Text.Json;
using FedTechSearch.Core.Qualifications;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FedTechSearch.Data.Tests;

[Collection(PostgresCollection.Name)]
public sealed class QualificationStorageTests(PostgresFixture db)
{
    [Fact]
    public async Task Qualification_info_round_trips_through_jsonb_and_matches_the_parser()
    {
        using var scope = db.Services.CreateScope();
        var jobs = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Jobs.AsNoTracking().ToListAsync();

        Assert.All(jobs, j => Assert.NotNull(j.QualificationInfo));
        foreach (var job in jobs)
        {
            var expected = QualificationParser.Parse(job.Qualifications, null, null, job.PayPlan, job.LowGrade, job.HighGrade).Info;
            // Details.Education and WhoMayApply aren't on Job, so compare the parts that come from the stored text.
            Assert.Equal(expected.Parsed, job.QualificationInfo!.Parsed);
            Assert.Equal(expected.StandardCompetencies, job.QualificationInfo.StandardCompetencies);
            Assert.Equal(expected.Blocks.SelectMany(b => b.ExperienceDuties), job.QualificationInfo.Blocks.SelectMany(b => b.ExperienceDuties));
        }
    }

    [Fact]
    public async Task Job_endpoint_includes_qualification_info()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Jobs", db.ConnectionString);
        using var api = new WebApplicationFactory<Program>();
        string id;
        using (var scope = db.Services.CreateScope())
            id = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Jobs.Select(j => j.Id).FirstAsync();

        var json = await api.CreateClient().GetFromJsonAsync<JsonElement>($"/api/jobs/{id}");

        Assert.True(json.TryGetProperty("qualificationInfo", out var info));
        Assert.True(info.TryGetProperty("parsed", out _));
        Assert.True(json.TryGetProperty("qualifications", out var original));
        Assert.False(string.IsNullOrEmpty(original.GetString())); // original text is kept
    }
}
