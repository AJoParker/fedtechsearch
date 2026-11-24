using FedTechSearch.Data.Alerts;

namespace FedTechSearch.Ingest;

/// <summary>
/// Checks every 15 minutes for alert digests that are due (daily at 07:00, weekly Monday 07:00,
/// local to Alerts:TimeZone) and sends them from whatever the 6-hourly ingest has loaded.
/// </summary>
public sealed class AlertWorker(IServiceScopeFactory scopes, ILogger<AlertWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<AlertDigestService>().RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Alert digest run failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
