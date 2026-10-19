using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bank.DAL.Context;

/// <summary>
/// Design-time factory used by the EF Core tooling (add-migration / update-database)
/// to create <see cref="AppDbContext"/> without booting the API host. The host requires
/// runtime configuration (JWT key, user secrets, database, Hangfire, ...), so relying on
/// its service provider makes migrations fail at design time.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string DefaultConnection =
        "Host=localhost;Port=5432;Database=bank_system;Username=postgres;Password=0000";

    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ResolveConnectionString(), npgsql => npgsql.UseNetTopologySuite())
            .Options;

        return new AppDbContext(options);
    }

    private static string ResolveConnectionString()
    {
        // Allows overriding the connection string when running the tooling (e.g. in CI).
        var fromEnvironment = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
            return fromEnvironment;

        var appSettingsPath = FindStartupAppSettings();
        if (appSettingsPath is not null)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(appSettingsPath));
                if (document.RootElement.TryGetProperty("ConnectionStrings", out var connections) &&
                    connections.TryGetProperty("DefaultConnection", out var value) &&
                    !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    return value.GetString()!;
                }
            }
            catch (JsonException)
            {
                // Fall back to the default connection string below.
            }
        }

        return DefaultConnection;
    }

    private static string? FindStartupAppSettings()
    {
        foreach (var origin in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(origin);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "Bank.API", "appsettings.json");
                if (File.Exists(candidate))
                    return candidate;

                if (directory.Name.Equals("Bank.API", StringComparison.OrdinalIgnoreCase))
                {
                    candidate = Path.Combine(directory.FullName, "appsettings.json");
                    if (File.Exists(candidate))
                        return candidate;
                }

                directory = directory.Parent;
            }
        }

        return null;
    }
}
