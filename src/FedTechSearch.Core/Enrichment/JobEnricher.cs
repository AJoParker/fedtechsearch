using FedTechSearch.Core.Taxonomy;

namespace FedTechSearch.Core.Enrichment;

public enum LlmMode
{
    /// <summary>Rules only.</summary>
    Off,

    /// <summary>Ask the LLM only when the rule-based role guess is weak.</summary>
    LowConfidence,

    /// <summary>Ask the LLM for every job (best titles, slowest).</summary>
    Always
}

public sealed record LlmClassification(string? WorkingTitle, string? RoleFamily, IReadOnlyList<string> Tech);

public interface ILlmClassifier
{
    Task<LlmClassification?> ClassifyAsync(
        RawJob job,
        EnrichedFields ruleFields,
        CompiledTaxonomy taxonomy,
        CancellationToken cancellationToken);
}

public sealed class NullLlmClassifier : ILlmClassifier
{
    public Task<LlmClassification?> ClassifyAsync(RawJob job, EnrichedFields ruleFields, CompiledTaxonomy taxonomy, CancellationToken cancellationToken)
        => Task.FromResult<LlmClassification?>(null);
}

/// <summary>
/// Hybrid enrichment: rules always run; the LLM fills in the working title and settles
/// the role family when the rules are unsure. LLM output is only accepted when it fits
/// the taxonomy, so facets stay clean.
/// </summary>
public sealed class JobEnricher
{
    /// <summary>Bump when extraction logic changes so stored jobs get re-enriched.</summary>
    public const int AlgorithmVersion = 2;

    private readonly CompiledTaxonomy _taxonomy;
    private readonly RuleExtractor _rules;
    private readonly ILlmClassifier _llm;
    private readonly LlmMode _mode;

    public JobEnricher(CompiledTaxonomy taxonomy, RuleExtractor rules, ILlmClassifier llm, LlmMode mode)
    {
        _taxonomy = taxonomy;
        _rules = rules;
        _llm = llm;
        _mode = mode;
    }

    public int EnrichmentVersion => _taxonomy.Version * 1000 + AlgorithmVersion * 10 + (int)_mode;

    public async Task<EnrichedFields> EnrichAsync(RawJob job, CancellationToken cancellationToken)
    {
        var fields = _rules.Extract(job);

        var askLlm = _mode switch
        {
            LlmMode.Always => true,
            LlmMode.LowConfidence => fields.LowConfidence || fields.WorkingTitleSource == WorkingTitleSource.Rules,
            _ => false
        };
        if (!askLlm) return fields;

        var llm = await _llm.ClassifyAsync(job, fields, _taxonomy, cancellationToken);
        if (llm is null) return fields;

        if (!string.IsNullOrWhiteSpace(llm.RoleFamily))
        {
            var match = _taxonomy.RoleFamilyNames.FirstOrDefault(n => n.Equals(llm.RoleFamily.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match is not null && (fields.LowConfidence || _mode == LlmMode.Always))
            {
                fields.RoleFamily = match;
                fields.LowConfidence = false;
            }
        }

        var title = llm.WorkingTitle?.Trim();
        if (!string.IsNullOrEmpty(title) && title.Length <= 80 && fields.WorkingTitleSource != WorkingTitleSource.Posting)
        {
            fields.WorkingTitle = title;
            fields.WorkingTitleSource = WorkingTitleSource.Llm;
        }

        foreach (var t in llm.Tech)
        {
            if (_taxonomy.CanonicalTechName.TryGetValue(t.Trim(), out var canonical) && !fields.Tech.Contains(canonical))
                fields.Tech.Add(canonical);
        }

        return fields;
    }
}
