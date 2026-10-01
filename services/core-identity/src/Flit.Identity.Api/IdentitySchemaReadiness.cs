using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Flit.Identity.Api;

/// <summary>
/// ¿El esquema tiene todo lo que este código usa? core-identity no migra (lo hace core-api), así que en un despliegue
/// por servicio puede arrancar un core-identity nuevo antes de que core-api migre. Para cada tabla y vista del modelo
/// de identidad se consulta <c>SELECT columnas FROM tabla LIMIT 0</c>: si falta una tabla o una columna, no está listo
/// y el gateway manda el login al respaldo. «Listo» se guarda 30 s y «no listo» 5 s, para no repetir las consultas en
/// cada sondeo.
/// </summary>
internal sealed class IdentitySchemaReadiness
{
    private static readonly TimeSpan ReadyFor = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan NotReadyFor = TimeSpan.FromSeconds(5);
    private (string? Problem, DateTimeOffset Until) _last = (null, DateTimeOffset.MinValue);

    /// <summary><c>null</c> si está listo; si no, el motivo: base inaccesible, esquema atrasado u otro error de la base.</summary>
    public async Task<string?> CheckAsync(IdentityDbContext db, CancellationToken ct)
    {
        var last = _last;
        if (last.Until > DateTimeOffset.UtcNow)
            return last.Problem;

        var problem = await ProbeAsync(db, ct).ConfigureAwait(false);
        _last = (problem, DateTimeOffset.UtcNow.Add(problem is null ? ReadyFor : NotReadyFor));
        return problem;
    }

    private static async Task<string?> ProbeAsync(IdentityDbContext db, CancellationToken ct)
    {
        try
        {
            if (!await db.Database.CanConnectAsync(ct).ConfigureAwait(false))
                return "database_unreachable";

            foreach (var probe in Probes(db.GetService<IDesignTimeModel>().Model))
            {
#pragma warning disable EF1002 // nombres de tablas y columnas del propio modelo, no entrada de usuario
                await db.Database.ExecuteSqlRawAsync(probe, ct).ConfigureAwait(false);
#pragma warning restore EF1002
            }

            return null;
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState is "42P01" or "42703")
        {
            return "schema_behind_code"; // falta una tabla (42P01) o una columna (42703) que este código usa
        }
        catch (Npgsql.PostgresException)
        {
            return "database_error";
        }
        catch (Npgsql.NpgsqlException)
        {
            return "database_unreachable";
        }
    }

    internal static IEnumerable<string> Probes(IModel model)
    {
        var relational = model.GetRelationalModel();
        IEnumerable<ITableBase> tables = [.. relational.Tables, .. relational.Views];
        return tables
            .Select(t => $"SELECT {string.Join(", ", t.Columns.Select(c => Quote(c.Name)))} FROM {(t.Schema is null ? string.Empty : Quote(t.Schema) + ".")}{Quote(t.Name)} LIMIT 0")
            .Distinct(StringComparer.Ordinal);
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
