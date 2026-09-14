using System.Net.Sockets;
using Npgsql;

namespace Flit.Integration.Tests.Postgres;

/// <summary>
/// HU #12319 (AC5) — decide, UNA sola vez por proceso, si hay un PostgreSQL alcanzable con la
/// connection string administrativa. Lo consultan <see cref="PostgresFactAttribute"/> /
/// <see cref="PostgresTheoryAttribute"/> (skip dinámico fuera de CI) y
/// <see cref="PostgresDatabaseFixture"/> (fallo explícito en CI).
/// </summary>
public static class PostgresAvailability
{
    /// <summary>Mensaje de skip (AC5). Es literal de contrato: las evidencias afirman sobre él.</summary>
    public const string SkipReason =
        "Requiere PostgreSQL alcanzable: define ConnectionStrings__Core o FLIT_IT_PG_ADMIN " +
        "(Host=...;Port=...;Username=...;Password=...;Database=postgres) o ConnectionStrings:Core " +
        "en src/Flit.Api/appsettings.Development.json. En CI (GITHUB_ACTIONS/CI=true) esto es un fallo, no un skip.";

    private static readonly Lazy<Probe> ProbeResult = new(RunProbe, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Connection string administrativa resuelta (o <c>null</c> si ninguna fuente aplica).</summary>
    internal static PostgresConnectionSource? Source => ProbeResult.Value.Source;

    /// <summary><c>true</c> si se pudo abrir la conexión administrativa.</summary>
    public static bool IsAvailable => ProbeResult.Value.Available;

    /// <summary>Motivo técnico de la no disponibilidad (sin contraseña), para el fallo en CI.</summary>
    public static string? UnavailableDetail => ProbeResult.Value.Detail;

    /// <summary>GitHub Actions y la mayoría de CIs exportan alguna de estas dos variables.</summary>
    public static bool IsCi =>
        IsTruthy(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")) ||
        IsTruthy(Environment.GetEnvironmentVariable("CI"));

    /// <summary>
    /// Lo que evalúa <c>SkipUnless</c> del atributo: fuera de CI, sin motor ⇒ skip. En CI el test
    /// SIEMPRE corre y es el fixture quien lanza el fallo con el detalle (AC5).
    /// </summary>
    public static bool ShouldRun => IsAvailable || IsCi;

    private static bool IsTruthy(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1";

    private static Probe RunProbe()
    {
        var source = PostgresConnectionResolver.Resolve();
        if (source is null)
        {
            return new Probe(
                null,
                false,
                "No hay connection string: ni FLIT_IT_PG_ADMIN, ni ConnectionStrings__Core, ni src/Flit.Api/appsettings.Development.json.");
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(source.AdminConnectionString) { Timeout = 5 };
            using var connection = new NpgsqlConnection(builder.ConnectionString);
            connection.Open();
            return new Probe(source, true, null);
        }
        catch (Exception ex) when (ex is NpgsqlException or SocketException or TimeoutException or ArgumentException)
        {
            return new Probe(
                source,
                false,
                $"No se pudo abrir {source.SafeDescription} (origen: {source.Origin}): {ex.GetType().Name}: {ex.Message}");
        }
    }

    private sealed record Probe(PostgresConnectionSource? Source, bool Available, string? Detail);
}
