using FedTechSearch.Core.Model;
using FedTechSearch.Data.Email;
using FedTechSearch.Data.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FedTechSearch.Data.Alerts;

public sealed record DigestRunResult(int Due, int Sent, int Empty, int Failed);

/// <summary>
/// Sends due alert digests. Each window is recorded in alert_deliveries before sending, so if the
/// worker stops after sending but before saving, the next run sees the window as sent and only
/// advances the alert instead of emailing again.
/// </summary>
public sealed class AlertDigestService(
    AppDbContext db,
    JobSearch search,
    AlertTokens tokens,
    IEmailSender email,
    TimeProvider time,
    IOptions<AlertOptions> alertOptions,
    IOptions<AppOptions> appOptions,
    ILogger<AlertDigestService> logger)
{
    public async Task<DigestRunResult> RunAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var zone = AlertSchedule.Zone(alertOptions.Value.TimeZone);
        var baseUrl = appOptions.Value.PublicBaseUrl.TrimEnd('/');

        // Housekeeping: expired tokens are useless and only grow the table.
        await db.AuthTokens.Where(t => t.ExpiresAt < now).ExecuteDeleteAsync(cancellationToken);

        // A plain list first; each due alert is loaded fresh (tracked) inside the loop, so a failure
        // that clears the change tracker can't detach the alerts still to come.
        var active = await db.SavedSearches.AsNoTracking()
            .Join(db.Subscribers, s => s.SubscriberId, sub => sub.Id, (s, sub) => new
            {
                s.Id, s.Frequency, s.LastNotifiedAt, s.ConfirmedAt, s.PausedAt, sub.Email,
                SubscriberConfirmed = sub.ConfirmedAt, sub.UnsubscribedAt
            })
            .Where(x => x.SubscriberConfirmed != null && x.UnsubscribedAt == null && x.ConfirmedAt != null && x.PausedAt == null)
            .ToListAsync(cancellationToken);

        int due = 0, sent = 0, empty = 0, failed = 0;
        foreach (var row in active)
        {
            if (!AlertSchedule.IsDue(row.Frequency, row.LastNotifiedAt, now, zone)) continue;
            due++;
            var windowStart = row.LastNotifiedAt!.Value;

            try
            {
                var alert = await db.SavedSearches.FirstAsync(x => x.Id == row.Id, cancellationToken);
                var delivery = await db.AlertDeliveries.FirstOrDefaultAsync(d => d.SavedSearchId == alert.Id && d.WindowStart == windowStart, cancellationToken);
                if (delivery?.SentAt is not null)
                {
                    // Sent before a restart: just catch the alert up.
                    alert.LastNotifiedAt = delivery.WindowEnd;
                    await db.SaveChangesAsync(cancellationToken);
                    continue;
                }

                var request = AlertQuery.Parse(alert.QueryString);
                request.NewSince = windowStart;
                request.Sort = "newest";
                request.Page = 1;
                request.PageSize = alertOptions.Value.DigestSize;
                var result = await search.SearchAsync(request, cancellationToken);

                if (result.Total == 0)
                {
                    alert.LastNotifiedAt = now;
                    await db.SaveChangesAsync(cancellationToken);
                    empty++;
                    continue;
                }

                if (delivery is null)
                {
                    delivery = new AlertDelivery { SavedSearchId = alert.Id, WindowStart = windowStart };
                    db.AlertDeliveries.Add(delivery);
                }
                delivery.WindowEnd = now;
                delivery.JobCount = result.Total;

                var links = new EmailLinks(
                    Manage: $"{baseUrl}/alerts?token={tokens.Issue(alert.SubscriberId, TokenPurpose.Manage, lifetime: AlertTokens.DigestManageLifetime)}",
                    Unsubscribe: $"{baseUrl}/alerts/unsubscribe?token={tokens.Issue(alert.SubscriberId, TokenPurpose.Unsubscribe)}",
                    Privacy: $"{baseUrl}/privacy");
                await db.SaveChangesAsync(cancellationToken); // the window is recorded before anything is sent

                var oneClick = links.Unsubscribe.Replace("/alerts/unsubscribe?", "/api/alerts/unsubscribe?", StringComparison.Ordinal);
                var (subject, html, text) = EmailTemplates.Digest(alert.Name, result.Items, result.Total, $"{baseUrl}/?{alert.QueryString}", links);
                await email.SendAsync(new EmailMessage(row.Email, subject, html, text, oneClick), cancellationToken);

                delivery.SentAt = time.GetUtcNow().UtcDateTime;
                alert.LastNotifiedAt = now;
                await db.SaveChangesAsync(cancellationToken);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Retried on the next tick. Only the error type: mail errors can contain the address.
                failed++;
                logger.LogWarning("Digest failed for alert {AlertId}: {Error}", row.Id, ex.GetType().Name);
                db.ChangeTracker.Clear();
            }
        }

        var summary = new DigestRunResult(due, sent, empty, failed);
        if (due > 0) logger.LogInformation("Alert digests: {Result}", summary);
        return summary;
    }
}
