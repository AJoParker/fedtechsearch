using FedTechSearch.Core.Enrichment;
using FedTechSearch.Core.Taxonomy;
using FedTechSearch.Data;
using FedTechSearch.Data.Search;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Services.AddFedTechSearch(builder.Configuration);
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:4200"])
    .AllowAnyHeader()
    .WithMethods("GET")));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DbInitializer.WaitForSchemaAsync(db, app.Logger);
}

app.UseCors();

var api = app.MapGroup("/api");

api.MapGet("/jobs", async ([AsParameters] JobSearchRequest request, JobSearch search, CancellationToken ct) =>
    Results.Ok(await search.SearchAsync(request, ct)));

api.MapGet("/jobs/{id}", async (string id, JobSearch search, CancellationToken ct) =>
    await search.GetAsync(id, ct) is { } job ? Results.Ok(job) : Results.NotFound());

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
