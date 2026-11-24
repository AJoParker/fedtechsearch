using System.Net.Mail;
using FedTechSearch.Core.Model;
using FedTechSearch.Data.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FedTechSearch.Data.Alerts;

public sealed record SavedSearchView(
    Guid Id, string Name, string QueryString, AlertFrequency Frequency, bool Confirmed, bool Paused, DateTime CreatedAt, DateTime? LastNotifiedAt);

public sealed record SubscriberView(string Email, bool Unsubscribed, IReadOnlyList<SavedSearchView> Searches);

public sealed record SearchPatch(string? Name, AlertFrequency? Frequency, bool? Paused);

/// <summary>
/// Sign-up, confirmation, sign-in links and management for email alerts. Public entry points
/// behave the same whether or not an address is known, so they can't be used to find out who has
/// alerts. Email addresses are never logged; logs use subscriber and search IDs.
/// </summary>
public sealed class AlertService(
    AppDbContext db,
    AlertTokens tokens,
    IEmailSender email,
    TimeProvider time,
    IOptions<AlertOptions> alertOptions,
    IOptions<AppOptions> appOptions,
    ILogger<AlertService> logger)
{
    public const int MaxNameLength = 80;
    private string BaseUrl => appOptions.Value.PublicBaseUrl.TrimEnd('/');
    private string PrivacyUrl => $"{BaseUrl}/privacy";

    /// <summary>Normalized address, or null if it isn't a plain, valid email.</summary>
    public static string? NormalizeEmail(string? input)
    {
        var trimmed = input?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > 254) return null;
        try
        {
            var address = new MailAddress(trimmed);
            return address.Address == trimmed && address.Host.Contains('.') ? trimmed.ToLowerInvariant() : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Saves an unconfirmed alert and emails a confirmation link. Returns an error only for input
    /// problems; known, unknown, capped and rate-limited addresses all look the same.
    /// </summary>
    public async Task<string?> SubscribeAsync(string? rawEmail, string? queryString, string? rawName, AlertFrequency frequency, CancellationToken cancellationToken)
    {
        var address = NormalizeEmail(rawEmail);
        if (address is null) return "Enter a valid email address.";
        if ((queryString?.Length ?? 0) > AlertQuery.MaxLength) return "That search is too long to save.";
        var name = (rawName ?? "").Trim();
        if (name.Length == 0) name = "My job search";
        if (name.Length > MaxNameLength) name = name[..MaxNameLength];
        var canonical = AlertQuery.Canonicalize(queryString);

        var now = time.GetUtcNow().UtcDateTime;
        var subscriber = await db.Subscribers.Include(s => s.Searches).FirstOrDefaultAsync(s => s.Email == address, cancellationToken);
        if (subscriber is null)
        {
            subscriber = new Subscriber { Email = address, CreatedAt = now };
            db.Subscribers.Add(subscriber);
        }

        var search = subscriber.Searches.FirstOrDefault(s => s.QueryString == canonical);
        if (search is null)
        {
            if (subscriber.Searches.Count >= alertOptions.Value.MaxSearchesPerSubscriber)
            {
                logger.LogInformation("Alert not added for subscriber {SubscriberId}: at the limit", subscriber.Id);
                await db.SaveChangesAsync(cancellationToken);
                return null;
            }
            search = new SavedSearch { SubscriberId = subscriber.Id, Name = name, QueryString = canonical, Frequency = frequency, CreatedAt = now };
            // Added explicitly: the Guid is set in code, so EF would otherwise take it for an existing row.
            db.SavedSearches.Add(search);
            subscriber.Searches.Add(search);
        }
        else if (search.ConfirmedAt is not null && subscriber.UnsubscribedAt is null)
        {
            await db.SaveChangesAsync(cancellationToken);
            return null; // already active; nothing to confirm
        }

        if (await SentTooManyAsync(subscriber.Id, cancellationToken))
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Confirmation not sent for subscriber {SubscriberId}: daily email limit", subscriber.Id);
            return null;
        }

        var raw = tokens.Issue(subscriber.Id, TokenPurpose.Confirm, search.Id);
        await db.SaveChangesAsync(cancellationToken);

        var (subject, html, text) = EmailTemplates.Confirm(search.Name, $"{BaseUrl}/alerts/confirm?token={raw}", PrivacyUrl);
        await TrySendAsync(new EmailMessage(address, subject, html, text), subscriber.Id, cancellationToken);
        return null;
    }

    /// <summary>Activates the subscriber and the alert behind a confirm link; returns a session token, or null.</summary>
    public async Task<string?> ConfirmAsync(string? raw, CancellationToken cancellationToken)
    {
        var token = await tokens.RedeemAsync(raw, TokenPurpose.Confirm, cancellationToken);
        if (token is null) return null;
        var now = time.GetUtcNow().UtcDateTime;
        var subscriber = await db.Subscribers.Include(s => s.Searches).FirstAsync(s => s.Id == token.SubscriberId, cancellationToken);
        subscriber.ConfirmedAt ??= now;
        subscriber.UnsubscribedAt = null;
        if (subscriber.Searches.FirstOrDefault(s => s.Id == token.SavedSearchId) is { } search)
        {
            search.ConfirmedAt ??= now;
            search.LastNotifiedAt ??= now; // the first digest holds only jobs found after confirming
        }
        var session = tokens.Issue(subscriber.Id, TokenPurpose.Session);
        await db.SaveChangesAsync(cancellationToken);
        return session;
    }

    /// <summary>Emails a sign-in link to a confirmed subscriber. Looks the same for unknown addresses.</summary>
    public async Task RequestManageLinkAsync(string? rawEmail, CancellationToken cancellationToken)
    {
        var address = NormalizeEmail(rawEmail);
        if (address is null) return;
        var subscriber = await db.Subscribers.FirstOrDefaultAsync(s => s.Email == address && s.ConfirmedAt != null, cancellationToken);
        if (subscriber is null || await SentTooManyAsync(subscriber.Id, cancellationToken)) return;

        var raw = tokens.Issue(subscriber.Id, TokenPurpose.Manage);
        await db.SaveChangesAsync(cancellationToken);
        var (subject, html, text) = EmailTemplates.ManageLink($"{BaseUrl}/alerts?token={raw}", PrivacyUrl);
        await TrySendAsync(new EmailMessage(address, subject, html, text), subscriber.Id, cancellationToken);
    }

    /// <summary>Trades a one-time sign-in link for a session token.</summary>
    public async Task<string?> StartSessionAsync(string? raw, CancellationToken cancellationToken)
    {
        var token = await tokens.RedeemAsync(raw, TokenPurpose.Manage, cancellationToken);
        if (token is null) return null;
        var session = tokens.Issue(token.SubscriberId, TokenPurpose.Session);
        await db.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task<Guid?> SubscriberForSessionAsync(string? raw, CancellationToken cancellationToken) =>
        (await tokens.RedeemAsync(raw, TokenPurpose.Session, cancellationToken))?.SubscriberId;

    public async Task<SubscriberView?> GetAsync(Guid subscriberId, CancellationToken cancellationToken)
    {
        var s = await db.Subscribers.AsNoTracking().Include(x => x.Searches).FirstOrDefaultAsync(x => x.Id == subscriberId, cancellationToken);
        return s is null ? null : new SubscriberView(s.Email, s.UnsubscribedAt is not null,
            s.Searches.OrderBy(x => x.CreatedAt).Select(View).ToList());
    }

    public async Task<SavedSearchView?> UpdateAsync(Guid subscriberId, Guid searchId, SearchPatch patch, CancellationToken cancellationToken)
    {
        var search = await db.SavedSearches.FirstOrDefaultAsync(x => x.Id == searchId && x.SubscriberId == subscriberId, cancellationToken);
        if (search is null) return null;
        if (patch.Name?.Trim() is { Length: > 0 } name) search.Name = name.Length > MaxNameLength ? name[..MaxNameLength] : name;
        if (patch.Frequency is { } frequency) search.Frequency = frequency;
        if (patch.Paused is { } paused) search.PausedAt = paused ? search.PausedAt ?? time.GetUtcNow().UtcDateTime : null;
        if (patch.Paused == false)
        {
            // Resuming doesn't send a backlog: the next digest covers jobs from now on.
            search.LastNotifiedAt = time.GetUtcNow().UtcDateTime;
        }
        await db.SaveChangesAsync(cancellationToken);
        return View(search);
    }

    public async Task<bool> DeleteSearchAsync(Guid subscriberId, Guid searchId, CancellationToken cancellationToken) =>
        await db.SavedSearches.Where(x => x.Id == searchId && x.SubscriberId == subscriberId).ExecuteDeleteAsync(cancellationToken) > 0;

    /// <summary>Deletes the address and everything tied to it (alerts, tokens, delivery records).</summary>
    public async Task DeleteSubscriberAsync(Guid subscriberId, CancellationToken cancellationToken)
    {
        await db.Subscribers.Where(x => x.Id == subscriberId).ExecuteDeleteAsync(cancellationToken);
        logger.LogInformation("Subscriber {SubscriberId} deleted their data", subscriberId);
    }

    /// <summary>One-click unsubscribe from all alerts. Idempotent; true if the link was valid.</summary>
    public async Task<bool> UnsubscribeAsync(string? raw, CancellationToken cancellationToken)
    {
        var token = await tokens.RedeemAsync(raw, TokenPurpose.Unsubscribe, cancellationToken);
        if (token is null) return false;
        var subscriber = await db.Subscribers.FirstAsync(s => s.Id == token.SubscriberId, cancellationToken);
        subscriber.UnsubscribedAt ??= time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<bool> SentTooManyAsync(Guid subscriberId, CancellationToken cancellationToken)
    {
        var since = time.GetUtcNow().UtcDateTime.AddDays(-1);
        var sent = await db.AuthTokens.CountAsync(t => t.SubscriberId == subscriberId && t.CreatedAt > since
            && (t.Purpose == TokenPurpose.Confirm || t.Purpose == TokenPurpose.Manage), cancellationToken);
        return sent >= alertOptions.Value.EmailsPerAddressPerDay;
    }

    private async Task TrySendAsync(EmailMessage message, Guid subscriberId, CancellationToken cancellationToken)
    {
        try
        {
            await email.SendAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The visitor still sees "check your email"; the link can be requested again. Only the
            // error type is logged: mail-server messages can contain the recipient's address.
            logger.LogWarning("Could not send alert email to subscriber {SubscriberId}: {Error}", subscriberId, ex.GetType().Name);
        }
    }

    private static SavedSearchView View(SavedSearch s) =>
        new(s.Id, s.Name, s.QueryString, s.Frequency, s.ConfirmedAt is not null, s.PausedAt is not null, s.CreatedAt, s.LastNotifiedAt);
}
