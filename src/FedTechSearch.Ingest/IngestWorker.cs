using FedTechSearch.Data;
using FedTechSearch.Data.Ingest;
using Microsoft.Extensions.Options;

namespace FedTechSearch.Ingest;

public sealed class IngestWorkerOptions
{
    public const string Section = "Ingest";

    /// <summary>USAJOBS updates throughout the day; every few hours keeps listings fresh without hammering the API.</summary>
    public double IntervalHours { get; set; } = 6;
}

public sealed class IngestWorker(
    IServiceScopeFactory scopes,
    IOptions<IngestWorkerOptions> options,
    ILogger<IngestWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (var scope = scopes.CreateScope())
        {
            await DbInitializer.MigrateAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), logger, stoppingToken);
        }

        var interval = TimeSpan.FromHours(Math.Max(0.25, options.Value.IntervalHours));
        using var timer = new PeriodicTimer(interval);

        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IngestService>().RunFromApiAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Keep the worker alive; the next tick tries again.
                logger.LogError(ex, "Ingest run failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
