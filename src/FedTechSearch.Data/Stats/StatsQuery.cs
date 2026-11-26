using Microsoft.EntityFrameworkCore;

namespace FedTechSearch.Data.Stats;

public sealed record StatCounts(int Active, int New, int Remote, int Telework);

public sealed record StatValue(string Value, int Active, int New, int Remote, int Telework, int? Change7d);

public sealed record StatPoint(DateOnly Date, int Active);

public sealed record StatSeries(string Value, IReadOnlyList<StatPoint> Points);

/// <summary>Null date and empty lists mean no snapshot has been taken yet.</summary>
public sealed record StatsResponse(
    string Dimension, DateOnly? Date, int Days, StatCounts Total, IReadOnlyList<StatValue> Top, IReadOnlyList<StatSeries> Series);

/// <summary>Reads <c>daily_stats</c> for the stats page: the latest snapshot's top values plus their daily series.</summary>
public sealed class StatsQuery(AppDbContext db)
{
    public const int DefaultDays = 90;
    public const int DefaultTop = 10;

    public static bool IsDimension(string? dimension) => dimension is not null && StatsDimensions.All.Contains(dimension);

    public async Task<StatsResponse> GetAsync(string dimension, int? days, int? top, CancellationToken cancellationToken)
    {
        var dayCount = Math.Clamp(days ?? DefaultDays, 1, 365);
        var topCount = Math.Clamp(top ?? DefaultTop, 1, 25);

        var latest = await db.DailyStats.Where(s => s.Dimension == dimension)
            .MaxAsync(s => (DateOnly?)s.Date, cancellationToken);
        if (latest is not { } date)
            return new StatsResponse(dimension, null, dayCount, new StatCounts(0, 0, 0, 0), [], []);

        var total = await db.DailyStats.AsNoTracking()
            .Where(s => s.Date == date && s.Dimension == StatsDimensions.Total && s.Value == StatsDimensions.TotalValue)
            .Select(s => new StatCounts(s.ActiveCount, s.NewCount, s.RemoteCount, s.TeleworkCount))
            .FirstOrDefaultAsync(cancellationToken) ?? new StatCounts(0, 0, 0, 0);

        var current = await db.DailyStats.AsNoTracking()
            .Where(s => s.Date == date && s.Dimension == dimension)
            .OrderByDescending(s => s.ActiveCount).ThenBy(s => s.Value)
            .Take(topCount)
            .ToListAsync(cancellationToken);
        var values = current.Select(s => s.Value).ToList();

        // Read back far enough for both the series and the week-ago comparison.
        var weekAgo = date.AddDays(-7);
        var from = date.AddDays(-(dayCount - 1));
        var since = from < weekAgo ? from : weekAgo;
        var history = await db.DailyStats.AsNoTracking()
            .Where(s => s.Dimension == dimension && s.Date >= since && s.Date < date && values.Contains(s.Value))
            .Select(s => new { s.Value, s.Date, s.ActiveCount })
            .ToListAsync(cancellationToken);

        // A value missing from an existing week-old snapshot had zero jobs; with no snapshot there's no change to show.
        var hadWeekAgo = await db.DailyStats.AnyAsync(s => s.Dimension == dimension && s.Date == weekAgo, cancellationToken);
        var topValues = current.Select(s => new StatValue(s.Value, s.ActiveCount, s.NewCount, s.RemoteCount, s.TeleworkCount,
            hadWeekAgo ? s.ActiveCount - (history.FirstOrDefault(h => h.Value == s.Value && h.Date == weekAgo)?.ActiveCount ?? 0) : null)).ToList();

        var series = current.Select(s => new StatSeries(s.Value,
            history.Where(h => h.Value == s.Value && h.Date >= from).OrderBy(h => h.Date).Select(h => new StatPoint(h.Date, h.ActiveCount))
                .Append(new StatPoint(date, s.ActiveCount)).ToList())).ToList();

        return new StatsResponse(dimension, date, dayCount, total, topValues, series);
    }
}
