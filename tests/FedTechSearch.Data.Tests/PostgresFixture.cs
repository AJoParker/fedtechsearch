using FedTechSearch.Data.Ingest;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace FedTechSearch.Data.Tests;

/// <summary>
/// A throwaway Postgres with the schema created by migrations and the sample fixture loaded
/// through the real ingest pipeline. Needs Docker.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    public ServiceProvider Services { get; private set; } = null!;

    /// <summary>"Now" for date filters. Fixture jobs open 2026-11-10 and close 2026-11-24.</summary>
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero));

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Jobs"] = _postgres.GetConnectionString(),
                ["Llm:Mode"] = "Off"
            })
            .Build();
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddFedTechIngest(config);
        Services = services.BuildServiceProvider();

        using var scope = Services.CreateScope();
        await DbInitializer.MigrateAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), NullLogger.Instance);
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "sample-search.json");
        await scope.ServiceProvider.GetRequiredService<IngestService>().RunFromFileAsync(path, CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await Services.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
