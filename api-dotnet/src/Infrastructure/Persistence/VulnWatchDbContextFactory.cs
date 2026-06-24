using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Persistence;

public class VulnWatchDbContextFactory : IDesignTimeDbContextFactory<VulnWatchDbContext>
{
    public VulnWatchDbContext CreateDbContext(string[] args)
    {
        var basePath = ResolveWebProjectPath();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnectionString")
                               ?? configuration.GetConnectionString("DefaultConnection")
                               ?? throw new InvalidOperationException("Default database connection string is not configured.");

        var optionsBuilder = new DbContextOptionsBuilder<VulnWatchDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new VulnWatchDbContext(optionsBuilder.Options);
    }

    private static string ResolveWebProjectPath()
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        var candidates = new[]
        {
            Path.Combine(currentDirectory, "..", "Web"),
            Path.Combine(currentDirectory, "Web"),
            Path.Combine(currentDirectory, "api-dotnet", "src", "Web")
        };

        foreach (var candidate in candidates.Select(Path.GetFullPath))
        {
            if (File.Exists(Path.Combine(candidate, "Web.csproj")))
                return candidate;
        }

        throw new InvalidOperationException("Could not locate the Web project for design-time DbContext creation.");
    }
}
