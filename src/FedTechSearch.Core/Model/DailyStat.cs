namespace FedTechSearch.Core.Model;

/// <summary>
/// One day's count of active jobs for one value of one dimension (e.g. tech = Python). Written by
/// the ingest worker after each run; the stats page reads current values and daily trends from it.
/// </summary>
public sealed class DailyStat
{
    /// <summary>Local date in the alerts time zone (Eastern by default).</summary>
    public DateOnly Date { get; set; }

    /// <summary>One of <c>StatsDimensions.All</c>: tech, roleFamily, agency, ... or total.</summary>
    public string Dimension { get; set; } = "";
    public string Value { get; set; } = "";

    /// <summary>Active jobs with this value.</summary>
    public int ActiveCount { get; set; }

    /// <summary>Active jobs with this value that opened in the 7 days ending on <see cref="Date"/>.</summary>
    public int NewCount { get; set; }

    /// <summary>Active jobs with this value that are fully remote.</summary>
    public int RemoteCount { get; set; }

    /// <summary>Active jobs with this value that are telework eligible (not fully remote).</summary>
    public int TeleworkCount { get; set; }
}
