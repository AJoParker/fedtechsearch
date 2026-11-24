using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FedTechSearch.Core.Enrichment;
using FedTechSearch.Core.Feedback;
using FedTechSearch.Core.Taxonomy;
using FedTechSearch.Core.Model;
using FedTechSearch.Data;
using FedTechSearch.Data.Alerts;
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
// Enums (alert frequency) travel as "Daily"/"Weekly", not numbers.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:4200"])
    .AllowAnyHeader()
    .WithMethods("GET", "POST", "PATCH", "DELETE")));

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
    // Sign-up and sign-in links send email, so they're limited hard (plus a per-address daily cap).
    o.AddPolicy("alerts", http => RateLimitPartition.GetFixedWindowLimiter(
        "alerts:" + (http.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = http.RequestServices.GetRequiredService<IConfiguration>().GetValue("Alerts:RateLimitPerHour", 5),
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

// ---- Email alerts ----------------------------------------------------------------------
// Sign-up and sign-in answer the same way whether or not an address is known.
var alerts = api.MapGroup("/alerts");

alerts.MapPost("", async (AlertSignup body, AlertService service, CancellationToken ct) =>
        await service.SubscribeAsync(body.Email, body.QueryString, body.Name, body.Frequency ?? AlertFrequency.Daily, ct) is { } error
            ? Results.BadRequest(new { error })
            : Results.Accepted(value: new { status = "check-email" }))
    .RequireRateLimiting("alerts")
    .WithMetadata(new RequestSizeLimitAttribute(8192));

alerts.MapPost("/manage-link", async (AlertEmail body, AlertService service, CancellationToken ct) =>
    {
        await service.RequestManageLinkAsync(body.Email, ct);
        return Results.Accepted(value: new { status = "check-email" });
    })
    .RequireRateLimiting("alerts")
    .WithMetadata(new RequestSizeLimitAttribute(2048));

alerts.MapPost("/confirm", async (AlertToken body, AlertService service, CancellationToken ct) =>
    await service.ConfirmAsync(body.Token, ct) is { } session
        ? Results.Ok(new { session })
        : Results.BadRequest(new { error = "This link has expired or was already used." }));

alerts.MapPost("/session", async (AlertToken body, AlertService service, CancellationToken ct) =>
    await service.StartSessionAsync(body.Token, ct) is { } session
        ? Results.Ok(new { session })
        : Results.BadRequest(new { error = "This sign-in link has expired or was already used." }));

// One-click unsubscribe (RFC 8058): mail providers POST here from the List-Unsubscribe header.
alerts.MapPost("/unsubscribe", async (string? token, AlertService service, CancellationToken ct) =>
    await service.UnsubscribeAsync(token, ct) ? Results.Ok(new { status = "unsubscribed" }) : Results.BadRequest(new { error = "This link isn't valid." }))
    .DisableAntiforgery();

// Everything below needs "Authorization: Bearer <session>" and only touches that subscriber's rows.
var mine = alerts.MapGroup("").AddEndpointFilter(async (context, next) =>
{
    var header = context.HttpContext.Request.Headers.Authorization.ToString();
    var raw = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : null;
    var service = context.HttpContext.RequestServices.GetRequiredService<AlertService>();
    if (await service.SubscriberForSessionAsync(raw, context.HttpContext.RequestAborted) is not { } subscriberId)
        return Results.Unauthorized();
    context.HttpContext.Items["SubscriberId"] = subscriberId;
    return await next(context);
});

static Guid Me(HttpContext http) => (Guid)http.Items["SubscriberId"]!;

mine.MapGet("", async (HttpContext http, AlertService service, CancellationToken ct) =>
    await service.GetAsync(Me(http), ct) is { } view ? Results.Ok(view) : Results.Unauthorized());

mine.MapPatch("/searches/{id:guid}", async (Guid id, SearchPatch patch, HttpContext http, AlertService service, CancellationToken ct) =>
    await service.UpdateAsync(Me(http), id, patch, ct) is { } view ? Results.Ok(view) : Results.NotFound());

mine.MapDelete("/searches/{id:guid}", async (Guid id, HttpContext http, AlertService service, CancellationToken ct) =>
    await service.DeleteSearchAsync(Me(http), id, ct) ? Results.NoContent() : Results.NotFound());

mine.MapDelete("/me", async (HttpContext http, AlertService service, CancellationToken ct) =>
{
    await service.DeleteSubscriberAsync(Me(http), ct);
    return Results.NoContent();
});

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

public sealed record AlertSignup(string? Email, string? QueryString, string? Name, AlertFrequency? Frequency);
public sealed record AlertEmail(string? Email);
public sealed record AlertToken(string? Token);
