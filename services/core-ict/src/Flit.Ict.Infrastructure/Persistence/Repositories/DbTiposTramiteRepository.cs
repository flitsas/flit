using System.Data;
using System.Data.Common;
using Flit.Ict.Domain.Trazabilidad;
using Microsoft.EntityFrameworkCore;

namespace Flit.Ict.Infrastructure.Persistence.Repositories;

/// <summary>
/// Catálogo de tipos de trámite para el filtro de la bandeja (HU #11815).
/// </summary>
/// <remarks>
/// <para>
/// La consulta parte de los trámites VISIBLES, no del catálogo maestro, y por eso arrastra los
/// mismos predicados de alcance que la bandeja. Dos motivos, y el segundo es el que manda:
/// </para>
/// <para>
/// 1. Un desplegable con los 20 tipos del catálogo ofrece 18 opciones que devuelven cero.
/// </para>
/// <para>
/// 2. Para una empresa, ese desplegable sería una filtración: la lista de tipos que NO tramita ella
/// solo puede venir de lo que tramitan las demás.
/// </para>
/// </remarks>
public sealed class DbTiposTramiteRepository(IctDbContext db) : ITiposTramiteQuery
{
    public async Task<IReadOnlyList<TipoTramiteOpcion>> ConsultarAsync(
        Guid? tenantId, Guid? companiaTenantId, CancellationToken ct = default)
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
                SELECT DISTINCT m.transaction_type, COALESCE(t.name, ''), COALESCE(pm.family, '')
                FROM ict.external_integration_master m
                LEFT JOIN ict.external_integration_procedure_type t ON t.id = m.transaction_type
                LEFT JOIN ict.procedure_type_mapping pm ON pm.external_transaction_type = m.transaction_type
                WHERE m.deleted_at IS NULL
                  AND (@tenant::uuid IS NULL OR m.tenant_id = @tenant::uuid)
                  AND (@compania::uuid IS NULL OR m.tenant_id = @compania::uuid)
                ORDER BY 2
                """;
            AddParam(cmd, "tenant", (object?)tenantId ?? DBNull.Value);
            AddParam(cmd, "compania", (object?)companiaTenantId ?? DBNull.Value);

            var opciones = new List<TipoTramiteOpcion>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var nombre = reader.GetString(1);
                // Un tipo sin nombre en el catálogo se muestra por su número en vez de como una
                // opción en blanco: sigue siendo seleccionable y quien lo vea sabe qué reportar.
                var familia = reader.GetString(2);
                opciones.Add(new TipoTramiteOpcion(
                    reader.GetInt32(0),
                    nombre.Length > 0 ? nombre : $"Tipo {reader.GetInt32(0)}",
                    // Un tipo sin mapeo cae en «otros» del lado del cliente. Aquí se devuelve vacío
                    // en vez de inventar una familia: el hueco es del mapeo, y disimularlo lo
                    // volvería invisible.
                    familia.Length > 0 ? familia : null));
            }

            return opciones;
        }
        finally
        {
            if (wasClosed)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
