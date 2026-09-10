using System.Data;
using System.Data.Common;
using Flit.Ict.Domain.Trazabilidad;
using Microsoft.EntityFrameworkCore;

namespace Flit.Ict.Infrastructure.Persistence.Repositories;

/// <summary>
/// Bandeja de trazabilidad ICT (HU #11815): una fila por PRE-TRÁMITE.
/// </summary>
/// <remarks>
/// <para>
/// SQL crudo y no LINQ a propósito: la consulta necesita el mismo <c>CASE</c> de estado en tres
/// sitios (proyección, filtro y conteos), un <c>GREATEST</c> sobre tres marcas de tiempo y una
/// comparación contra un arreglo de placas. Expresarlo en LINQ obliga a traducciones frágiles y
/// esconde el plan de consulta, que aquí importa: la tabla tiene cientos de miles de filas.
/// </para>
/// <para>
/// <b>Aislamiento por tenant.</b> La garantía real es el predicado de aplicación
/// (<c>tenant_id = @tenant</c>), no RLS. Las tablas del schema <c>ict</c> llevan
/// <c>ENABLE ROW LEVEL SECURITY</c> pero NO <c>FORCE</c>, y core-ict conecta con el rol dueño de
/// esas tablas: para el dueño la política no se aplica salvo que se fuerce. Se fija igualmente el
/// GUC <c>app.current_tenant_id</c> como defensa en profundidad, para que el día que se despliegue
/// con un rol no-dueño la consulta siga siendo correcta en vez de vaciarse.
/// </para>
/// </remarks>
public sealed class DbTrazabilidadBandejaRepository(IctDbContext db) : ITrazabilidadBandejaQuery
{
    /// <summary>
    /// Proyección del estado v2 en SQL.
    /// </summary>
    /// <remarks>
    /// <b>Espeja a propósito</b> <c>IctEstado.Map</c>. Están duplicados porque el filtro y los
    /// contadores tienen que resolverse en el motor —traer cientos de miles de filas a memoria para
    /// clasificarlas en C# no es una opción—, mientras que la API de estado del cliente sigue usando
    /// el método de dominio. Si uno cambia y el otro no, el contador de la tira deja de cuadrar con
    /// lo que devuelve la bandeja. <c>IctEstadoMapTests</c> fija el comportamiento de la versión C#
    /// para que un cambio en ella rompa una prueba y obligue a mirar aquí.
    /// </remarks>
    internal const string EstadoSql = """
        CASE
            WHEN m.procedure_instance_id IS NOT NULL THEN 'borrador_creado'
            WHEN m.process_status_id = 1 THEN 'recibido'
            WHEN m.process_status_id = 2 AND m.business_validation <> 2 THEN 'en_validacion_negocio'
            WHEN m.process_status_id = 2 THEN 'en_validacion_externa'
            WHEN m.process_status_id = 3 THEN 'procesado'
            WHEN m.process_status_id = 4 THEN 'con_novedades'
            ELSE 'anulado'
        END
        """;

    /// <summary>
    /// CTE base. Cierra en <c>filas</c> SIN un SELECT final: quien lo consume le concatena su propio
    /// filtro y su propia proyección. Añadirle un SELECT aquí rompe las dos consultas que lo usan.
    /// </summary>
    private const string BaseSql = $"""
        WITH filas AS (
            SELECT
                m.id,
                m.transaction_number,
                m.manager_id_transaction,
                m.plate,
                m.vin,
                m.transaction_type,
                m.transaction_operation,
                m.tenant_id,
                m.manager_user,
                m.created_at,
                m.starts_procedure_in_paused,
                m.process_without_attached_documents,
                (m.procedure_instance_id IS NOT NULL) AS tiene_tramite,
                pt.name AS tipo_tramite,
                ot.name AS operacion,
                t.legal_name AS compania,
                {EstadoSql} AS estado,
                -- Última señal de avance registrada. Se usa para el tiempo en espera: interesa cuánto
                -- lleva SIN MOVERSE, no cuánto lleva desde que entró.
                GREATEST(
                    m.created_at,
                    COALESCE(m.business_date_validation, m.created_at),
                    COALESCE(m.external_date_validation, m.created_at)
                ) AS ultimo_avance
            FROM ict.external_integration_master m
            LEFT JOIN ict.external_integration_procedure_type pt ON pt.id = m.transaction_type
            LEFT JOIN ict.external_integration_operation_type ot ON ot.id = m.transaction_operation
            LEFT JOIN identity.tenants t ON t.id = m.tenant_id
            WHERE m.deleted_at IS NULL
              AND (@tenant::uuid IS NULL OR m.tenant_id = @tenant::uuid)
              AND (@compania::uuid IS NULL OR m.tenant_id = @compania::uuid)
              AND (@numero::bigint IS NULL OR m.transaction_number = @numero::bigint)
              AND (@tipo::int IS NULL OR m.transaction_type = @tipo::int)
              -- Familia: el desplegable ofrece «toda la familia» además de los tipos sueltos. El
              -- mapeo es quien sabe a qué familia pertenece cada tipo de transacción de ICT.
              AND (@familia::text IS NULL OR EXISTS (
                  SELECT 1 FROM ict.procedure_type_mapping pm
                   WHERE pm.external_transaction_type = m.transaction_type
                     AND pm.family = @familia::text))
              AND (@operacion::int IS NULL OR m.transaction_operation = @operacion::int)
              AND (@desde::timestamptz IS NULL OR m.created_at >= @desde::timestamptz)
              AND (@hasta::timestamptz IS NULL OR m.created_at <= @hasta::timestamptz)
              -- Placas y VIN comparten campo en la interfaz porque el analista pega lo que le mandan
              -- sin distinguir cuál es cuál. UPPER en ambos lados: la BD guarda el dato como llegó.
              AND (
                  @placas::text[] IS NULL
                  OR UPPER(m.plate) = ANY(@placas::text[])
                  OR UPPER(COALESCE(m.vin, '')) = ANY(@placas::text[])
              )
        )
        """;

    public async Task<TrazabilidadPagina> ConsultarAsync(TrazabilidadFiltro filtro, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filtro);

        var pageSize = Math.Clamp(filtro.PageSize, 1, 200);
        var page = Math.Max(filtro.Page, 1);
        // Un estado desconocido se descarta en vez de devolver cero filas: la bandeja llega con el
        // estado en la URL y una URL vieja o retocada a mano no debe parecer una bandeja vacía.
        var estado = TrazabilidadEstados.EsValido(filtro.Estado) ? filtro.Estado : null;

        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed)
        {
            await connection.OpenAsync(ct);
        }

        try
        {
            await SetTenantGucAsync(connection, filtro.TenantId, ct);

            // Los contadores IGNORAN el filtro de estado y respetan todos los demás: la tira de arriba
            // tiene que seguir diciendo cuántos hay en cada estado mientras se navega dentro de uno.
            var conteos = await LeerConteosAsync(connection, filtro, ct);
            var total = estado is null
                ? conteos.Values.Sum()
                : conteos.GetValueOrDefault(estado);

            var items = await LeerPaginaAsync(connection, filtro, estado, page, pageSize, ct);

            return new TrazabilidadPagina(items, (int)total, page, pageSize, conteos);
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

    private static async Task<IReadOnlyDictionary<string, long>> LeerConteosAsync(
        DbConnection connection, TrazabilidadFiltro filtro, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = BaseSql + "\nSELECT estado, COUNT(*) FROM filas GROUP BY estado";
        AddFiltroParams(cmd, filtro);

        // Se siembran los siete en cero para que la tira dibuje siempre los siete contadores; sin esto,
        // un estado sin trámites desaparecería de la pantalla y el usuario no sabría que existe.
        var conteos = TrazabilidadEstados.Todos.ToDictionary(e => e, _ => 0L, StringComparer.Ordinal);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            conteos[reader.GetString(0)] = reader.GetInt64(1);
        }

        return conteos;
    }

    private static async Task<IReadOnlyList<TrazabilidadFila>> LeerPaginaAsync(
        DbConnection connection, TrazabilidadFiltro filtro, string? estado,
        int page, int pageSize, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = BaseSql + """

            SELECT id, transaction_number, manager_id_transaction, plate, vin,
                   transaction_type, tipo_tramite, transaction_operation, operacion,
                   tenant_id, compania, manager_user, estado,
                   CASE
                       WHEN estado IN ('borrador_creado', 'anulado') THEN NULL
                       ELSE FLOOR(EXTRACT(EPOCH FROM (now() - ultimo_avance)) / 60)::bigint
                   END AS minutos_esperando,
                   starts_procedure_in_paused, process_without_attached_documents,
                   tiene_tramite, created_at
            FROM filas
            WHERE (@estado::text IS NULL OR estado = @estado::text)
            ORDER BY created_at DESC, transaction_number DESC
            OFFSET @offset LIMIT @limit
            """;
        AddFiltroParams(cmd, filtro);
        AddParam(cmd, "estado", (object?)estado ?? DBNull.Value);
        AddParam(cmd, "offset", (page - 1) * pageSize);
        AddParam(cmd, "limit", pageSize);

        var filas = new List<TrazabilidadFila>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            filas.Add(new TrazabilidadFila(
                Id: reader.GetGuid(0),
                Numero: reader.GetInt64(1),
                ReferenciaCliente: await reader.IsDBNullAsync(2, ct) ? null : reader.GetString(2),
                Placa: reader.GetString(3),
                Vin: await reader.IsDBNullAsync(4, ct) ? null : reader.GetString(4),
                TipoTramiteId: reader.GetInt32(5),
                TipoTramite: await reader.IsDBNullAsync(6, ct) ? null : reader.GetString(6),
                OperacionId: reader.GetInt32(7),
                Operacion: await reader.IsDBNullAsync(8, ct) ? null : reader.GetString(8),
                ClientTenantId: reader.GetGuid(9),
                Compania: await reader.IsDBNullAsync(10, ct) ? null : reader.GetString(10),
                Radicador: await reader.IsDBNullAsync(11, ct) ? string.Empty : reader.GetString(11),
                Estado: reader.GetString(12),
                MinutosEsperando: await reader.IsDBNullAsync(13, ct) ? null : reader.GetInt64(13),
                Pausado: reader.GetBoolean(14),
                SinAdjuntos: reader.GetBoolean(15),
                TieneTramiteFlit: reader.GetBoolean(16),
                RecibidoEn: reader.GetDateTime(17)));
        }

        return filas;
    }

    private static void AddFiltroParams(DbCommand cmd, TrazabilidadFiltro filtro)
    {
        AddParam(cmd, "tenant", (object?)filtro.TenantId ?? DBNull.Value);
        AddParam(cmd, "compania", (object?)filtro.CompaniaTenantId ?? DBNull.Value);
        AddParam(cmd, "numero", (object?)filtro.Numero ?? DBNull.Value);
        AddParam(cmd, "tipo", (object?)filtro.TipoTramite ?? DBNull.Value);
        AddParam(cmd, "familia", (object?)filtro.Familia ?? DBNull.Value);
        AddParam(cmd, "operacion", (object?)filtro.Operacion ?? DBNull.Value);
        AddParam(cmd, "desde", (object?)filtro.Desde ?? DBNull.Value);
        AddParam(cmd, "hasta", (object?)filtro.Hasta ?? DBNull.Value);

        var placas = filtro.PlacasOVins;
        AddParam(cmd, "placas",
            placas is null || placas.Count == 0 ? DBNull.Value : placas.ToArray());
    }

    // El GUC se fija con scope de SESIÓN (is_local=false) porque los dos comandos corren sobre la misma
    // conexión sin transacción explícita, igual que en IctStatusV2Query. Para el SuperAdmin (tenant
    // null) se limpia: no hay un tenant al que acotar y el alcance lo decide el predicado @tenant.
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
