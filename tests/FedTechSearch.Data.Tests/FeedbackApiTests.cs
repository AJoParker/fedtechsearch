using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FedTechSearch.Data.Tests;

/// <summary>The real API (rate limiting, admin key, status codes) against the test database.</summary>
[Collection(PostgresCollection.Name)]
public sealed class FeedbackApiTests(PostgresFixture db)
{
    private const string AdminKey = "test-admin-key";

    private WebApplicationFactory<Program> Api(string? adminKey = AdminKey, int perHour = 1000)
    {
        // Read while the API registers services, before any test override applies.
        Environment.SetEnvironmentVariable("ConnectionStrings__Jobs", db.ConnectionString);
        return new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("Admin:ApiKey", adminKey ?? "")
            .UseSetting("Feedback:RateLimitPerHour", perHour.ToString()));
    }

    private async Task<(string Id, string Tech)> AnyJob()
    {
        using var scope = db.Services.CreateScope();
        var job = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Jobs.AsNoTracking().FirstAsync(j => j.Tech.Count > 0);
        return (job.Id, job.Tech[^1]);
    }

    [Fact]
    public async Task Feedback_returns_204_400_and_404()
    {
        var (id, tech) = await AnyJob();
        using var api = Api();
        var client = api.CreateClient();

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync($"/api/jobs/{id}/feedback", new { field = "tech", currentValue = tech })).StatusCode);
        var bad = await client.PostAsJsonAsync($"/api/jobs/{id}/feedback", new { field = "salary", currentValue = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("Unknown field", await bad.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync("/api/jobs/does-not-exist/feedback", new { field = "tech", currentValue = tech })).StatusCode);
    }

    [Fact]
    public async Task Feedback_is_rate_limited_per_client()
    {
        var (id, tech) = await AnyJob();
        using var api = Api(perHour: 3);
        var client = api.CreateClient();

        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
            codes.Add((await client.PostAsJsonAsync($"/api/jobs/{id}/feedback", new { field = "tech", currentValue = tech })).StatusCode);

        Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.NoContent, HttpStatusCode.NoContent, HttpStatusCode.TooManyRequests], codes);
    }

    [Fact]
    public async Task Summary_needs_the_admin_key_and_is_off_without_one()
    {
        using (var disabled = Api(adminKey: null))
            Assert.Equal(HttpStatusCode.NotFound, (await disabled.CreateClient().GetAsync("/api/admin/feedback/summary")).StatusCode);

        using var api = Api();
        var client = api.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/feedback/summary")).StatusCode);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/feedback/summary?days=30");
        request.Headers.Add("X-Admin-Key", "wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(request)).StatusCode);

        request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/feedback/summary?days=30");
        request.Headers.Add("X-Admin-Key", AdminKey);
        var ok = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Contains("\"groups\"", await ok.Content.ReadAsStringAsync());
    }
}
