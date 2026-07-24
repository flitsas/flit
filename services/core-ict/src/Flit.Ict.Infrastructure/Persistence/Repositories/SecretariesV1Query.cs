using System.Data;
using System.Data.Common;
using Flit.Ict.Domain.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Flit.Ict.Infrastructure.Persistence.Repositories;

/// <summary>
/// Secretarías de tránsito activas (compatibilidad v1). Lee el catálogo global
/// <c>catalogs.transit_offices</c> (mismo Postgres, cross-schema; sin RLS por ser catálogo global,
/// igual que las lecturas de <c>analytics.*</c> de las alertas). Devuelve <c>code</c> y <c>name</c>.
/// </summary>
public sealed class SecretariesV1Query(IctDbContext db) : ISecretariesV1Query
{
    public async Task<IReadOnlyList<SecretaryV1>> GetActiveAsync(CancellationToken ct = default)
    {
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed)
        {
            await connection.OpenAsync(ct);
        }

        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT code, name
                FROM catalogs.transit_offices
                WHERE is_active = true
                ORDER BY name
                """;
            var list = new List<SecretaryV1>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                list.Add(new SecretaryV1(
                    reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    reader.IsDBNull(1) ? string.Empty : reader.GetString(1)));
            }

            return list;
        }
        finally
        {
            if (wasClosed)
            {
                await connection.CloseAsync();
            }
        }
    }
}
