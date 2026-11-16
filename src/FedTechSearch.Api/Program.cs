using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using FedTechSearch.Core.Enrichment;
using FedTechSearch.Core.Feedback;
using FedTechSearch.Core.Taxonomy;
using FedTechSearch.Data;
using FedTechSearch.Data.Feedback;
using FedTechSearch.Data.Search;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Services.AddFedTechSearch(builder.Configuration);
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:4200"])
    .AllowAnyHeader()
    .WithMethods("GET", "POST")));

// Behind nginx the connection comes from the proxy; trust X-Forwarded-For only from private
// networks (the Docker network, loopback) so rate limiting and hashing see the real visitor.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
    o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("10.0.0.0/8"));
    o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("172.16.0.0/12"));
    o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("192.168.0.0/16"));
});

// Public endpoints that write are rate limited per client (roadmap ground rule).
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("feedback", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = http.RequestServices.GetRequiredService<IConfiguration>().GetValue("Feedback:RateLimitPerHour", 20),
            Window = TimeSpan.FromHours(1),
            QueueLimit = 0
        }));
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DbInitializer.WaitForSchemaAsync(db, app.Logger);
}

app.UseForwardedHeaders();
app.UseCors();
app.UseRateLimiter();

var api = app.MapGroup("/api");

api.MapGet("/jobs", async ([AsParameters] JobSearchRequest request, JobSearch search, CancellationToken ct) =>
    Results.Ok(await search.SearchAsync(request, ct)));

api.MapGet("/jobs/{id}", async (string id, JobSearch search, CancellationToken ct) =>
    await search.GetAsync(id, ct) is { } job ? Results.Ok(job) : Results.NotFound());

// "Wrong tag?" reports. 204 also covers same-day repeats, so a client can't tell they were deduplicated.
api.MapPost("/jobs/{id}/feedback", async (string id, FeedbackInput input, HttpContext http, FeedbackService feedback, CancellationToken ct) =>
    {
        var result = await feedback.SubmitAsync(id, input, http.Connection.RemoteIpAddress?.ToString(), ct);
        return result.Outcome switch
        {
            SubmitOutcome.JobNotFound => Results.NotFound(),
            SubmitOutcome.Invalid => Results.BadRequest(new { error = result.Error }),
            _ => Results.NoContent()
        };
    })
    .RequireRateLimiting("feedback")
    .WithMetadata(new RequestSizeLimitAttribute(4096));

// The report used to fix taxonomy patterns. Disabled (404) unless Admin:ApiKey is set.
api.MapGet("/admin/feedback/summary", async (int? days, FeedbackService feedback, CancellationToken ct) =>
        Results.Ok(await feedback.SummaryAsync(days ?? 90, 3, ct)))
    .AddEndpointFilter(async (context, next) =>
    {
        var configured = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Admin:ApiKey"];
        if (string.IsNullOrWhiteSpace(configured)) return Results.NotFound();
        var sent = context.HttpContext.Request.Headers["X-Admin-Key"].ToString();
        var ok = CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(sent)), SHA256.HashData(Encoding.UTF8.GetBytes(configured)));
        return ok ? await next(context) : Results.Unauthorized();
    });

// Everything a UI needs to render filter controls, straight from the taxonomy.
api.MapGet("/meta", (CompiledTaxonomy taxonomy) => Results.Ok(new
{
    taxonomyVersion = taxonomy.Version,
    roleFamilies = taxonomy.RoleFamilies
        .Select(f => new { name = f.Definition.Name, workingTitle = f.Definition.WorkingTitle })
        .Append(new { name = CompiledTaxonomy.GeneralFamily, workingTitle = CompiledTaxonomy.GeneralWorkingTitle }),
    tech = taxonomy.Tech
        .GroupBy(t => t.Definition.Category)
        .Select(g => new { category = g.Key, names = g.Select(t => t.Definition.Name) }),
    specialties = taxonomy.Specialties.Select(s => new { code = s.Code, name = s.Name }),
    seniority = Seniority.All,
    work = WorkArrangement.All,
    clearance = Clearance.All,
    hiringPaths = HiringPath.All,
    schedules = ScheduleType.All,
    appointments = AppointmentType.All,
    travel = TravelRequirement.All
}));

app.MapGet("/healthz", () => Results.Ok("ok"));

app.Run();

/// <summary>Visible to WebApplicationFactory in the API tests.</summary>
public partial class Program;
