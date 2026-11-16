namespace FedTechSearch.Core.Model;

/// <summary>A visitor's report that a derived tag on a job is wrong.</summary>
public sealed class TagFeedback
{
    public long Id { get; set; }
    public string JobId { get; set; } = "";

    /// <summary>roleFamily, tech, workingTitle, seniority, clearance or workArrangement.</summary>
    public string Field { get; set; } = "";

    /// <summary>The value shown on the job. Null for "a technology is missing".</summary>
    public string? CurrentValue { get; set; }

    /// <summary>The right value, in canonical taxonomy spelling. Null means "remove this tag" or "not sure".</summary>
    public string? SuggestedValue { get; set; }

    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>taxonomy.json version at report time, so fixed patterns can be told apart from open ones.</summary>
    public int TaxonomyVersion { get; set; }

    /// <summary>HMAC of IP + day for abuse control. Never the raw IP; unlinkable across days.</summary>
    public string ClientHash { get; set; } = "";
}
