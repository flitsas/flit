using System.Collections.Concurrent;
using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Flit.Identity.Api;

/// <summary>
/// ¿El esquema tiene todo lo que este código usa? core-identity no migra (lo hace core-api), así que en un despliegue
/// por servicio puede arrancar un core-identity nuevo antes de que core-api migre. Para cada tabla y vista del modelo
/// de identidad se consulta <c>SELECT columnas FROM tabla LIMIT 0</c>: si falta una tabla o una columna, no está listo
/// y el gateway manda el login al respaldo. El resultado positivo se guarda 30 s.
/// </summary>
internal sealed class IdentitySchemaReadiness
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _readyUntil = new(StringComparer.Ordinal);

    public async Task<string?> CheckAsync(IdentityDbContext db, CancellationToken ct)
    {
        if (_readyUntil.TryGetValue(nameof(IdentitySchemaReadiness), out var until) && until > DateTimeOffset.UtcNow)
            return null;

        if (!await db.Database.CanConnectAsync(ct).ConfigureAwait(false))
            return "database_unreachable";

        foreach (var probe in Probes(db.GetService<IDesignTimeModel>().Model))
        {
            try
            {
#pragma warning disable EF1002 // nombres de tablas y columnas del propio modelo, no entrada de usuario
                await db.Database.ExecuteSqlRawAsync(probe, ct).ConfigureAwait(false);
#pragma warning restore EF1002
            }
            catch (Npgsql.PostgresException)
            {
                return "schema_behind_code";
            }
        }

        _readyUntil[nameof(IdentitySchemaReadiness)] = DateTimeOffset.UtcNow.Add(CacheFor);
        return null;
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
