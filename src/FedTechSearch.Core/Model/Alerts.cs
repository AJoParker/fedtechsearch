namespace FedTechSearch.Core.Model;

/// <summary>Someone who asked for email alerts. The address is the only personal data the app stores.</summary>
public sealed class Subscriber
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Trimmed and lowercased. Never logged.</summary>
    public string Email { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? UnsubscribedAt { get; set; }
    public List<SavedSearch> Searches { get; set; } = [];
}

public enum AlertFrequency { Daily, Weekly }

public sealed class SavedSearch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public string Name { get; set; } = "";

    /// <summary>The search page's query string (same keys as the URL), without page or sort.</summary>
    public string QueryString { get; set; } = "";
    public AlertFrequency Frequency { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Each alert is confirmed by email, so nobody can attach searches to someone else's address.</summary>
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? PausedAt { get; set; }

    /// <summary>End of the last window that was emailed (or checked with no results).</summary>
    public DateTime? LastNotifiedAt { get; set; }
}

public enum TokenPurpose { Confirm, Manage, Session, Unsubscribe }

/// <summary>A magic-link or session token. Only the SHA-256 hash is stored; the raw value is emailed once.</summary>
public sealed class AuthToken
{
    public long Id { get; set; }
    public Guid SubscriberId { get; set; }
    public Guid? SavedSearchId { get; set; }
    public string TokenHash { get; set; } = "";
    public TokenPurpose Purpose { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
}

/// <summary>One digest window for one alert. Unique per window, so a restarted worker never sends twice.</summary>
public sealed class AlertDelivery
{
    public long Id { get; set; }
    public Guid SavedSearchId { get; set; }
    public DateTime WindowStart { get; set; }
    public DateTime WindowEnd { get; set; }
    public int JobCount { get; set; }
    public DateTime? SentAt { get; set; }
}
