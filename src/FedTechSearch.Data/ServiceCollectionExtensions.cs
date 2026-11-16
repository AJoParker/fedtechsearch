using FedTechSearch.Core.Enrichment;
using FedTechSearch.Core.Taxonomy;
using FedTechSearch.Data.Ingest;
using FedTechSearch.Data.Llm;
using FedTechSearch.Data.Search;
using FedTechSearch.Data.UsaJobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FedTechSearch.Data;

public static class ServiceCollectionExtensions
{
    /// <summary>Database, taxonomy and search. Used by the API.</summary>
    public static IServiceCollection AddFedTechSearch(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("Jobs")
                               ?? throw new InvalidOperationException("ConnectionStrings:Jobs is not set.");
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));

        var taxonomyPath = config["TaxonomyPath"];
        services.AddSingleton(_ => string.IsNullOrWhiteSpace(taxonomyPath)
            ? CompiledTaxonomy.LoadEmbedded()
            : CompiledTaxonomy.FromFile(taxonomyPath));

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<JobSearch>();
        return services;
    }

    /// <summary>Everything above plus the USAJOBS client, enrichment and ingest. Used by the worker.</summary>
    public static IServiceCollection AddFedTechIngest(this IServiceCollection services, IConfiguration config)
    {
        services.AddFedTechSearch(config);

        services.Configure<UsaJobsOptions>(config.GetSection(UsaJobsOptions.Section));
        services.Configure<LlmOptions>(config.GetSection(LlmOptions.Section));
        services.Configure<RuleExtractorOptions>(config.GetSection("Rules"));

        services.AddHttpClient<UsaJobsClient>(c => c.Timeout = TimeSpan.FromMinutes(2));
        services.AddHttpClient<OpenAiCompatibleClassifier>();

        services.AddSingleton(sp => new RuleExtractor(
            sp.GetRequiredService<CompiledTaxonomy>(),
            sp.GetRequiredService<IOptions<RuleExtractorOptions>>().Value));

        services.AddScoped<ILlmClassifier>(sp =>
            sp.GetRequiredService<IOptions<LlmOptions>>().Value.Mode == LlmMode.Off
                ? (ILlmClassifier)new NullLlmClassifier()
                : sp.GetRequiredService<OpenAiCompatibleClassifier>());

        services.AddScoped(sp => new JobEnricher(
            sp.GetRequiredService<CompiledTaxonomy>(),
            sp.GetRequiredService<RuleExtractor>(),
            sp.GetRequiredService<ILlmClassifier>(),
            sp.GetRequiredService<IOptions<LlmOptions>>().Value.Mode));

        services.AddScoped<IngestService>();
        return services;
    }
}
