using System.Text.Json;
using EFCore.NamingConventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Flit.Infrastructure.Persistence;

/// <summary>
/// Factory de diseño para herramientas EF Core (<c>dotnet ef</c>, <c>pnpm migrate</c>).
/// Resuelve <c>ConnectionStrings:Core</c> desde <c>Flit.Api/appsettings.Development.json</c>.
/// </summary>
public sealed class FlitDbContextFactory : IDesignTimeDbContextFactory<FlitDbContext>
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=flit_dev;Username=postgres;Password=postgres";

    public FlitDbContext CreateDbContext(string[] args)
    {
        var connectionString = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
            ? args[0]
            : ResolveConnectionString();

        var opts = new DbContextOptionsBuilder<FlitDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new FlitDbContext(opts);
    }

    private static string ResolveConnectionString()
    {
        var fromEnv = Environment.GetEnvironmentVariable("ConnectionStrings__Core");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
        }

        var apiDir = FindApiProjectDirectory();
        if (apiDir is not null)
        {
            var fromDev = ReadCoreConnectionString(Path.Combine(apiDir, "appsettings.Development.json"));
            if (!string.IsNullOrWhiteSpace(fromDev))
            {
                return fromDev;
            }

            var fromBase = ReadCoreConnectionString(Path.Combine(apiDir, "appsettings.json"));
            if (!string.IsNullOrWhiteSpace(fromBase))
            {
                return fromBase;
            }
        }

        return DefaultConnectionString;
    }

    private static string? FindApiProjectDirectory()
    {
        foreach (var startDir in GetCandidateStartDirectories())
        {
            for (var dir = startDir; dir is not null; dir = dir.Parent)
            {
                if (dir.Name.Equals("Flit.Api", StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(Path.Combine(dir.FullName, "appsettings.json")))
                {
                    return dir.FullName;
                }

                var siblingApi = Path.Combine(dir.FullName, "Flit.Api");
                if (File.Exists(Path.Combine(siblingApi, "appsettings.json")))
                {
                    return siblingApi;
                }
            }
        }

        return null;
    }

    private static IEnumerable<DirectoryInfo> GetCandidateStartDirectories()
    {
        yield return new DirectoryInfo(Directory.GetCurrentDirectory());

        var infraDir = Path.GetDirectoryName(typeof(FlitDbContextFactory).Assembly.Location);
        if (!string.IsNullOrWhiteSpace(infraDir))
        {
            yield return new DirectoryInfo(infraDir);
        }
    }

    private static string? ReadCoreConnectionString(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("ConnectionStrings", out var connectionStrings) ||
            !connectionStrings.TryGetProperty("Core", out var core))
        {
            return null;
        }

        return core.GetString();
    }
}
