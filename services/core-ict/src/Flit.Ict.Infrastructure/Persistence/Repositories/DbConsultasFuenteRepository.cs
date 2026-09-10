using System.Data;
using System.Data.Common;
using Flit.Ict.Domain.Trazabilidad;
using Flit.Ict.Infrastructure.Logging;
using Microsoft.EntityFrameworkCore;

namespace Flit.Ict.Infrastructure.Persistence.Repositories;

/// <summary>
/// Consultas a fuentes externas de un pre-trámite (HU #11817).
/// </summary>
/// <remarks>
/// <para>
/// A diferencia del log HTTP, aquí NO hace falta ninguna tabla puente:
/// <c>ict.external_integration_source_query.eim_id</c> apunta directamente al master con clave
/// foránea, y la respuesta cuelga de la consulta por <c>eisq_id</c>. La relación es uno a muchos.
/// Es la razón por la que esta pantalla se puede construir sin tocar el modelo de datos, y es donde
/// está la causa raíz de la mayoría de novedades.
/// </para>
/// <para>
/// <b>Enmascarado al servir.</b> La respuesta cruda del RUNT trae nombres, documentos y direcciones
/// completas. Se enmascara aquí, en el borde de lectura, y no solo al capturar: los datos ya
/// almacenados vienen de un tiempo en que esa barrera no existía, así que confiar en la captura
/// dejaría PII en claro para los registros antiguos.
/// </para>
/// <para>
/// Se usa <c>MaskJsonBody</c> (recursivo) y NO <c>MaskJson</c> pese a que este último es «el de
/// servir». <c>MaskJson</c> está pensado para objetos PLANOS: aplasta cualquier valor anidado a
/// cadena y, sobre todo, solo mira las claves del primer nivel. La respuesta del RUNT es un árbol
/// (<c>licenses</c>, <c>owner</c>, <c>technicalData</c>…) con nombres y documentos DENTRO de esos
/// arreglos, así que con el plano saldrían en claro y además ilegibles.
/// </para>
/// </remarks>
public sealed class DbConsultasFuenteRepository(IctDbContext db) : IConsultasFuenteQuery
{
    public async Task<IReadOnlyList<ConsultaFuente>?> ConsultarAsync(
        long numero, Guid? tenantId, CancellationToken ct = default)
    {
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed)
        {
            await connection.OpenAsync(ct);
        }

        try
        {
            await SetTenantGucAsync(connection, tenantId, ct);

            var masterId = await ResolverMasterAsync(connection, numero, tenantId, ct);
            if (masterId is null)
            {
                // Trámite inexistente o de otro tenant. El endpoint traduce ambos al mismo 404; una
                // lista vacía significaría «existe pero no ha consultado nada», que es otra cosa.
                return null;
            }

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT q.id, q.actor_level, q.query_type,
                       -- El identificador de la consulta es el documento, la placa o el VIN según el
                       -- tipo. Se resuelve aquí para que la tabla tenga una sola columna en vez de tres
                       -- casi siempre vacías.
                       COALESCE(NULLIF(q.document_number, ''), NULLIF(q.plate_complete, ''), NULLIF(q.vehicle_vin, '')) AS identificador,
                       q.document_type, q.is_data_queried, q.is_data_valid, q.attempts, q.created_at,
                       r.query_response
                FROM ict.external_integration_source_query q
                LEFT JOIN LATERAL (
                    -- La respuesta MÁS RECIENTE: un reintento deja varias, y lo que importa es qué
                    -- contestó la fuente la última vez.
                    SELECT sr.query_response
                    FROM ict.external_integration_source_response sr
                    WHERE sr.eisq_id = q.id
                    ORDER BY sr.created_at DESC
                    LIMIT 1
                ) r ON TRUE
                WHERE q.eim_id = @master
                ORDER BY q.created_at, q.id
                """;
            AddParam(cmd, "master", masterId.Value);

            var consultas = new List<ConsultaFuente>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var nivel = await reader.IsDBNullAsync(1, ct) ? string.Empty : reader.GetString(1);
                var tipo = await reader.IsDBNullAsync(2, ct) ? string.Empty : reader.GetString(2);
                var identificador = await reader.IsDBNullAsync(3, ct) ? null : reader.GetString(3);
                var tipoDocumento = await reader.IsDBNullAsync(4, ct) ? null : reader.GetString(4);
                var consultada = reader.GetBoolean(5);
                var valida = reader.GetBoolean(6);
                var intentos = await reader.IsDBNullAsync(7, ct) ? 0 : reader.GetInt32(7);
                var respuesta = await reader.IsDBNullAsync(9, ct) ? null : reader.GetString(9);

                consultas.Add(new ConsultaFuente(
                    Id: reader.GetGuid(0),
                    NivelActor: nivel,
                    NivelActorEtiqueta: EtiquetasConsultaFuente.NivelActor(nivel),
                    TipoConsulta: tipo,
                    TipoConsultaEtiqueta: EtiquetasConsultaFuente.TipoConsulta(tipo),
                    Identificador: Enmascarar(identificador, tipoDocumento),
                    Consultada: consultada,
                    Valida: valida,
                    Intentos: intentos,
                    Bloquea: EtiquetasConsultaFuente.Bloquea(consultada, valida, intentos),
                    CreadaEn: reader.GetDateTime(8),
                    Respuesta: IctSensitiveDataMasker.MaskJsonBody(respuesta)));
            }

            return consultas;
        }
        finally
        {
            await ResetTenantGucAsync(connection);
            if (wasClosed)
            {
                await connection.CloseAsync();
            }
        }
    }

    /// <summary>
    /// Enmascara el identificador conservando los últimos cuatro caracteres y el tipo de documento.
    /// </summary>
    /// <remarks>
    /// Con eso basta para que soporte confirme que la consulta se hizo sobre la persona correcta
    /// cuando el cliente le dicta el documento por teléfono, sin que la pantalla exponga el número
    /// entero a cualquiera que pase por detrás. Las placas y los VIN NO son datos personales y viajan
    /// completos: son la llave con la que el analista busca.
    /// </remarks>
    private static string? Enmascarar(string? identificador, string? tipoDocumento)
    {
        if (string.IsNullOrWhiteSpace(identificador))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(tipoDocumento))
        {
            return identificador;
        }

        var visible = identificador.Length <= 4
            ? identificador
            : string.Concat(new string('*', identificador.Length - 4), identificador[^4..]);

        return $"{tipoDocumento} {visible}";
    }

    private static async Task<Guid?> ResolverMasterAsync(
        DbConnection connection, long numero, Guid? tenantId, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id FROM ict.external_integration_master
            WHERE transaction_number = @numero
              AND deleted_at IS NULL
              AND (@tenant::uuid IS NULL OR tenant_id = @tenant::uuid)
            LIMIT 1
            """;
        AddParam(cmd, "numero", numero);
        AddParam(cmd, "tenant", (object?)tenantId ?? DBNull.Value);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is Guid id ? id : null;
    }

    private static async Task SetTenantGucAsync(DbConnection connection, Guid? tenantId, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT set_config('app.current_tenant_id', @tenant, false)";
        AddParam(cmd, "tenant", tenantId?.ToString() ?? string.Empty);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task ResetTenantGucAsync(DbConnection connection)
    {
        try
        {
            if (connection.State != ConnectionState.Open)
            {
                return;
            }

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT set_config('app.current_tenant_id', '', false)";
            await cmd.ExecuteNonQueryAsync(CancellationToken.None);
        }
#pragma warning disable CA1031 // limpiar el GUC es best-effort; nunca debe romper la consulta
        catch (Exception)
        {
            // best-effort
        }
#pragma warning restore CA1031
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
