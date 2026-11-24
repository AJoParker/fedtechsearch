using FedTechSearch.Data.Email;
using FedTechSearch.Data.Ingest;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

    public string ConnectionString => _postgres.GetConnectionString();

    /// <summary>Every email the app tried to send, instead of sending it.</summary>
    public CapturingEmailSender Emails { get; } = new();

    /// <summary>"Now" for date filters. Fixture jobs open 2026-11-10 and close 2026-11-24.</summary>
    public TestClock Clock { get; } = new(new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero));

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
        services.AddSingleton<IEmailSender>(Emails);
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

public sealed class CapturingEmailSender : IEmailSender
{
    private readonly List<EmailMessage> _sent = [];

    /// <summary>Set to make the next send throw (simulates a mail outage).</summary>
    public bool FailNext { get; set; }

    public IReadOnlyList<EmailMessage> Sent { get { lock (_sent) return _sent.ToList(); } }

    public void Clear() { lock (_sent) _sent.Clear(); }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (FailNext) { FailNext = false; throw new InvalidOperationException("mail server down"); }
        lock (_sent) _sent.Add(message);
        return Task.CompletedTask;
    }
}

/// <summary>
/// A clock tests can set to any time, backwards included (FakeTimeProvider only moves forward,
/// which breaks when test classes sharing the fixture pick their own dates).
/// </summary>
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void SetUtcNow(DateTimeOffset value) => _now = value;

    public void Advance(TimeSpan by) => _now += by;
}
