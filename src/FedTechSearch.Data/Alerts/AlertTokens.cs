using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using FedTechSearch.Core.Model;
using Microsoft.EntityFrameworkCore;

namespace FedTechSearch.Data.Alerts;

/// <summary>
/// Magic-link and session tokens: 32 random bytes, emailed or returned once, stored only as a
/// SHA-256 hash. Confirm and Manage tokens are single use; Session and Unsubscribe are reusable
/// until they expire, so an old email's unsubscribe link still works.
/// </summary>
public sealed class AlertTokens(AppDbContext db, TimeProvider time)
{
    public static readonly IReadOnlyDictionary<TokenPurpose, TimeSpan> Lifetimes = new Dictionary<TokenPurpose, TimeSpan>
    {
        [TokenPurpose.Confirm] = TimeSpan.FromHours(48),
        [TokenPurpose.Manage] = TimeSpan.FromHours(1),
        [TokenPurpose.Session] = TimeSpan.FromHours(12),
        [TokenPurpose.Unsubscribe] = TimeSpan.FromDays(120),
    };

    /// <summary>Manage links inside digests are read later than a link someone just asked for.</summary>
    public static readonly TimeSpan DigestManageLifetime = TimeSpan.FromDays(7);

    private static bool SingleUse(TokenPurpose purpose) => purpose is TokenPurpose.Confirm or TokenPurpose.Manage;

    /// <summary>Adds a token to the context (saved with the caller's next SaveChanges) and returns the raw value.</summary>
    public string Issue(Guid subscriberId, TokenPurpose purpose, Guid? savedSearchId = null, TimeSpan? lifetime = null)
    {
        var raw = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var now = time.GetUtcNow().UtcDateTime;
        db.AuthTokens.Add(new AuthToken
        {
            SubscriberId = subscriberId, SavedSearchId = savedSearchId, Purpose = purpose, TokenHash = Hash(raw),
            CreatedAt = now, ExpiresAt = now + (lifetime ?? Lifetimes[purpose])
        });
        return raw;
    }

    /// <summary>The live token for this raw value and purpose, or null. Single-use tokens are marked used (caller saves).</summary>
    public async Task<AuthToken?> RedeemAsync(string? raw, TokenPurpose purpose, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 100) return null;
        var hash = Hash(raw.Trim());
        var now = time.GetUtcNow().UtcDateTime;
        var token = await db.AuthTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (token is null || token.Purpose != purpose || token.ExpiresAt <= now) return null;
        if (SingleUse(purpose))
        {
            if (token.UsedAt is not null) return null;
            token.UsedAt = now;
        }
        return token;
    }

    public static string Hash(string raw) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
