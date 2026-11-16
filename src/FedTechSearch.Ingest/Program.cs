using FedTechSearch.Data;
using FedTechSearch.Data.Ingest;
using FedTechSearch.Ingest;

// Usage:
//   dotnet run                       run on a schedule (Ingest:IntervalHours)
//   dotnet run -- --once             one pull from the USAJOBS API, then exit
//   dotnet run -- --fixture <file>   load a saved USAJOBS response (no API key needed), then exit
var once = args.Contains("--once");
var fixtureIndex = Array.IndexOf(args, "--fixture");
var fixturePath = fixtureIndex >= 0 && fixtureIndex + 1 < args.Length ? args[fixtureIndex + 1] : null;

// Our own flags are parsed above, so they are not passed to the configuration command-line parser.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    // Find appsettings.json next to the binary even when run from the repo root.
    ContentRootPath = AppContext.BaseDirectory
});
builder.Services.AddFedTechIngest(builder.Configuration);
builder.Services.Configure<IngestWorkerOptions>(builder.Configuration.GetSection(IngestWorkerOptions.Section));

if (once || fixturePath is not null)
{
    using var host = builder.Build();
    using var scope = host.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DbInitializer.MigrateAsync(db, logger);

    var ingest = scope.ServiceProvider.GetRequiredService<IngestService>();
    var result = fixturePath is not null
        ? await ingest.RunFromFileAsync(fixturePath, CancellationToken.None)
        : await ingest.RunFromApiAsync(CancellationToken.None);
    Console.WriteLine($"Done. Seen {result.Seen}, enriched {result.Enriched}, unchanged {result.Unchanged}, retired {result.Deactivated}.");
    return;
}

builder.Services.AddHostedService<IngestWorker>();
await builder.Build().RunAsync();
