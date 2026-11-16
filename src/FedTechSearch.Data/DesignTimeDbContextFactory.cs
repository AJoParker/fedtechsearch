using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FedTechSearch.Data;

/// <summary>Lets `dotnet ef` build the context without starting the API or the worker.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Jobs")
                               ?? "Host=localhost;Database=fedtech;Username=fedtech;Password=fedtech";
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);
    }
}
