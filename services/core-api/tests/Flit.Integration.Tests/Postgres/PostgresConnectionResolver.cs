using System.Text.Json;
using Npgsql;

namespace Flit.Integration.Tests.Postgres;

/// <summary>
/// HU #12319 — resuelve la connection string <b>administrativa</b> con la que el arnés crea y borra
/// la base efímera. Orden de precedencia (AC5):
/// <list type="number">
///   <item><c>FLIT_IT_PG_ADMIN</c> (variable de entorno, connection string completa).</item>
///   <item><c>ConnectionStrings__Core</c> (variable de entorno; es la que exporta el CI).</item>
///   <item><c>ConnectionStrings:Core</c> de <c>src/Flit.Api/appsettings.Development.json</c>
///   (desarrollo local; se localiza subiendo desde <see cref="AppContext.BaseDirectory"/>).</item>
/// </list>
/// En 2 y 3 la base de la cadena se sustituye por <c>postgres</c>: la base de trabajo del
/// desarrollador (<c>flit_local</c>) o la del CI (<c>flit_dev</c>) <b>nunca</b> se toca; solo se usa
/// el servidor para crear/borrar la efímera. La contraseña no se expone en ningún miembro público.
/// </summary>
internal static class PostgresConnectionResolver
{
    public const string AdminEnvVar = "FLIT_IT_PG_ADMIN";
    public const string CoreEnvVar = "ConnectionStrings__Core";
    public const string AdminDatabase = "postgres";

    public static PostgresConnectionSource? Resolve()
    {
        var fromAdmin = Environment.GetEnvironmentVariable(AdminEnvVar);
        if (!string.IsNullOrWhiteSpace(fromAdmin))
        {
            return new PostgresConnectionSource(AdminEnvVar, fromAdmin);
        }

        var fromCore = Environment.GetEnvironmentVariable(CoreEnvVar);
        if (!string.IsNullOrWhiteSpace(fromCore))
        {
            return new PostgresConnectionSource(CoreEnvVar, WithDatabase(fromCore, AdminDatabase));
        }

        var appsettings = LocateDevelopmentAppSettings();
        if (appsettings is not null)
        {
            var fromFile = ReadCoreConnectionString(appsettings);
            if (!string.IsNullOrWhiteSpace(fromFile))
            {
                return new PostgresConnectionSource(
                    "src/Flit.Api/appsettings.Development.json (ConnectionStrings:Core)",
                    WithDatabase(fromFile, AdminDatabase));
            }
        }

        return null;
    }

    /// <summary>Cadena con la base sustituida (conserva host, puerto, usuario y contraseña).</summary>
    public static string WithDatabase(string connectionString, string database)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Database = database };
        return builder.ConnectionString;
    }

    /// <summary>Descripción segura para logs e informes: host, puerto, usuario y base. Nunca la contraseña.</summary>
    public static string Describe(string connectionString)
    {
        var b = new NpgsqlConnectionStringBuilder(connectionString);
        return $"Host={b.Host};Port={b.Port};Username={b.Username};Database={b.Database}";
    }

    private static string? LocateDevelopmentAppSettings()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Flit.Api", "appsettings.Development.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static string? ReadCoreConnectionString(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("ConnectionStrings", out var connectionStrings) ||
            !connectionStrings.TryGetProperty("Core", out var core))
        {
            return null;
        }

        return core.GetString();
    }
}

/// <summary>Connection string administrativa resuelta y de dónde salió (para el mensaje de skip / informe).</summary>
internal sealed record PostgresConnectionSource(string Origin, string AdminConnectionString)
{
    public string SafeDescription => PostgresConnectionResolver.Describe(AdminConnectionString);
}
