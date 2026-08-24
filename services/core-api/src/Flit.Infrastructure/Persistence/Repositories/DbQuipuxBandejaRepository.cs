using System.Data.Common;
using System.Globalization;
using System.Text;
using Flit.Modules.Quipux.Domain.LogQx;
using Flit.Tramites.Domain.Tramites.Estados;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Bandeja del LOG QX (HU #11786): trámites con integración Quipux, uno por fila, con filtros
/// combinables y contadores por estado.
///
/// <para><b>Por qué SQL crudo y no LINQ.</b> El predicado de elegibilidad depende de
/// <c>procedure_types.external_refs -&gt; 'quipux'</c>, un jsonb que EF no mapea — el mismo motivo por
/// el que <see cref="QuipuxSubmissionRepository.ListElegiblesSinSubmissionAsync"/> ya usa SQL. A eso
/// se suman un <c>DISTINCT ON</c> por trámite (la radicación más reciente) y la agregación de
/// contadores, que en LINQ saldrían como varias idas a la base.</para>
///
/// <para><b>Acceso cross-tenant</b>, igual que <see cref="DbQuipuxLogRepository"/>: el rol de core-api
/// es propietario de las tablas y estas no declaran <c>FORCE ROW LEVEL SECURITY</c>, así que la
/// política <c>tenant_isolation</c> no se le aplica. El acotado lo dan los filtros explícitos.</para>
///
/// <para><b>El universo no es <c>quipux_submissions</c>.</b> Son los trámites cuyo TIPO declara
/// integración Quipux: los que ya tienen radicación, más los que son elegibles y todavía no se
/// encolaron. Estos últimos son los <c>sin_radicar</c> y hoy no se pueden diagnosticar en ninguna
/// versión del módulo.</para>
/// </summary>
internal sealed class DbQuipuxBandejaRepository(FlitDbContext db) : IQuipuxBandejaRepository
{
    /// <summary>
    /// Universo + derivaciones. Cierra en el CTE <c>filas</c> SIN un SELECT final: quien lo consume
    /// le concatena el CTE <c>filtradas</c> (con el WHERE de los filtros) y luego su propio SELECT.
    /// De ahí salen tanto la página como los contadores, para que ambos vean exactamente el mismo
    /// conjunto filtrado y los totales no puedan discrepar de lo que se ve.
    /// </summary>
    /// <remarks>
    /// <c>elegibles</c> replica el predicado del worker (<see cref="QuipuxSubmissionRepository"/>):
    /// bandera de la secretaría POR FAMILIA declarada en <c>external_refs</c> y DIVIPO presente. Se
    /// mantiene deliberadamente idéntico — si divergen, la bandeja mostraría como elegible algo que
    /// el worker nunca va a encolar, que es peor que no mostrarlo.
    /// </remarks>
    private const string BaseSql = """
        WITH universo AS (
            SELECT pi.id                AS procedure_instance_id,
                   pi.tenant_id,
                   pi.reference_number,
                   pi.procedure_type_id,
                   pi.transit_office_id,
                   pi.status            AS tramite_status
            FROM tramites.procedure_instances pi
            JOIN tramites.procedure_types pt ON pt.id = pi.procedure_type_id
            JOIN catalogs.transit_offices o  ON o.id = pi.transit_office_id
            WHERE pi.deleted_at IS NULL
              AND pt.external_refs -> 'quipux' IS NOT NULL
              AND (
                    EXISTS (SELECT 1 FROM tramites.quipux_submissions s
                             WHERE s.procedure_instance_id = pi.id)
                 OR (
                        pi.status = @estado_preparado
                    AND o.is_active
                    AND NULLIF(o.divipo_code, '') IS NOT NULL
                    AND CASE pt.external_refs -> 'quipux' ->> 'familia'
                          WHEN 'MATRICULA' THEN o.quipux_registration
                          WHEN 'TRASPASO'  THEN o.quipux_transfer
                          WHEN 'OTROS'     THEN o.quipux_other
                          ELSE false
                        END
                    )
              )
        ),
        ultima AS (
            SELECT DISTINCT ON (s.procedure_instance_id)
                   s.procedure_instance_id, s.id, s.document_name, s.divipo_code, s.status,
                   s.qx_register_code, s.qx_procedure_code, s.rejection_reason,
                   s.attempts, s.poll_count, s.created_at, s.updated_at
            FROM tramites.quipux_submissions s
            ORDER BY s.procedure_instance_id, s.created_at DESC, s.id DESC
        ),
        intentos AS (
            SELECT procedure_instance_id, COUNT(*)::int AS n
            FROM tramites.quipux_submissions
            GROUP BY procedure_instance_id
        ),
        -- Última actividad real: el evento más reciente de CUALQUIERA de sus radicaciones.
        actividad AS (
            SELECT s.procedure_instance_id, MAX(e.occurred_at) AS last_event
            FROM tramites.quipux_submission_events e
            JOIN tramites.quipux_submissions s ON s.id = e.submission_id
            GROUP BY s.procedure_instance_id
        ),
        placas AS (
            SELECT DISTINCT ON (fv.procedure_instance_id)
                   fv.procedure_instance_id, fv.value_text
            FROM tramites.procedure_instance_field_values fv
            WHERE fv.value_text IS NOT NULL
              AND fv.value_text <> ''
              AND (LOWER(fv.field_key) LIKE '%plac%' OR LOWER(fv.field_key) LIKE '%plate%')
            ORDER BY fv.procedure_instance_id, fv.created_at
        ),
        -- Desde cuándo un trámite es elegible: su entrada MÁS RECIENTE a 'preparado'.
        -- No requiere persistencia nueva (ADR-0051).
        preparado AS (
            SELECT DISTINCT ON (h.procedure_instance_id)
                   h.procedure_instance_id, h.changed_at
            FROM tramites.procedure_instance_status_history h
            WHERE h.to_status = @estado_preparado
            ORDER BY h.procedure_instance_id, h.changed_at DESC
        ),
        filas AS (
            SELECT u.procedure_instance_id,
                   u.reference_number,
                   pl.value_text                              AS placa,
                   pt.name                                    AS tipo,
                   t.legal_name                               AS empresa,
                   o.name                                     AS secretaria,
                   o.id                                       AS transit_office_id,
                   u.tenant_id,
                   u.procedure_type_id,
                   COALESCE(ul.divipo_code, o.divipo_code)    AS divipo_code,
                   ul.document_name                           AS documento_qx,
                   ul.id                                      AS submission_id,
                   COALESCE(it.n, 0)                          AS intentos,
                   COALESCE(ul.attempts, 0)                   AS attempts,
                   COALESCE(ul.poll_count, 0)                 AS poll_count,
                   ul.qx_register_code,
                   ul.qx_procedure_code,
                   ul.rejection_reason,
                   ul.created_at                              AS submission_created_at,
                   CASE
                     WHEN ul.id IS NULL THEN 'sin_radicar'
                     WHEN ul.status = 'registrado' AND COALESCE(ul.poll_count, 0) = 0 THEN 'radicado'
                     WHEN ul.status = 'registrado' THEN 'en_tramite'
                     ELSE ul.status
                   END                                        AS estado,
                   COALESCE(ac.last_event, ul.updated_at, ul.created_at) AS ultima_actividad,
                   CASE
                     WHEN ul.id IS NULL THEN pr.changed_at
                     WHEN ul.status IN ('pendiente', 'registrado') THEN ul.created_at
                     ELSE NULL
                   END                                        AS esperando_desde
            FROM universo u
            JOIN tramites.procedure_types pt  ON pt.id = u.procedure_type_id
            JOIN catalogs.transit_offices o   ON o.id  = u.transit_office_id
            JOIN identity.tenants t           ON t.id  = u.tenant_id
            LEFT JOIN ultima ul    ON ul.procedure_instance_id = u.procedure_instance_id
            LEFT JOIN intentos it  ON it.procedure_instance_id = u.procedure_instance_id
            LEFT JOIN actividad ac ON ac.procedure_instance_id = u.procedure_instance_id
            LEFT JOIN placas pl    ON pl.procedure_instance_id = u.procedure_instance_id
            LEFT JOIN preparado pr ON pr.procedure_instance_id = u.procedure_instance_id
        )
        """;

    public async Task<QuipuxBandejaPage> SearchAsync(
        QuipuxBandejaQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!db.Database.IsRelational())
        {
            // Tests con InMemory: el predicado depende del jsonb external_refs y de DISTINCT ON, sin
            // equivalente LINQ. La cobertura real es de integración contra Postgres.
            return new QuipuxBandejaPage([], 0, []);
        }

        var (where, parameters) = BuildFilter(query);

        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        var contadores = await ReadContadoresAsync(connection, where, parameters, cancellationToken)
            .ConfigureAwait(false);

        var total = contadores.Sum(c => c.Total);
        if (total == 0)
        {
            return new QuipuxBandejaPage([], 0, contadores);
        }

        var entries = await ReadPageAsync(connection, where, parameters, query, cancellationToken)
            .ConfigureAwait(false);

        return new QuipuxBandejaPage(entries, total, contadores);
    }

    /// <summary>
    /// Contadores sobre el conjunto filtrado COMPLETO — nunca sobre la página (AC6). Siempre
    /// devuelve las siete claves, con cero donde no hay filas: una bandeja que oculta el contador de
    /// «Fallido» cuando vale cero obliga a adivinar si es cero o si no se calculó.
    /// </summary>
    private static async Task<IReadOnlyList<QuipuxBandejaContador>> ReadContadoresAsync(
        DbConnection connection,
        string where,
        IReadOnlyList<(string Name, object Value)> parameters,
        CancellationToken ct)
    {
        var sql = $"{BaseSql}{where} SELECT estado, COUNT(*)::int FROM filtradas GROUP BY estado";

        var porEstado = new Dictionary<string, int>(StringComparer.Ordinal);

        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            Prepare(cmd, sql, parameters);
            var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    porEstado[reader.GetString(0)] = reader.GetInt32(1);
                }
            }
        }

        return QuipuxBandejaEstados.Todos
            .Select(e => new QuipuxBandejaContador(e, porEstado.GetValueOrDefault(e)))
            .ToList();
    }

    /// <summary>
    /// Página ordenada por última actividad descendente. <c>NULLS LAST</c> es deliberado: un
    /// <c>sin_radicar</c> no tiene actividad, y dejarlo arriba por ser null lo pondría antes que
    /// trámites que sí se movieron hace un minuto.
    /// </summary>
    private static async Task<IReadOnlyList<QuipuxBandejaEntry>> ReadPageAsync(
        DbConnection connection,
        string where,
        IReadOnlyList<(string Name, object Value)> parameters,
        QuipuxBandejaQuery query,
        CancellationToken ct)
    {
        var sql = $"""
            {BaseSql}{where}
            SELECT * FROM filtradas
            ORDER BY ultima_actividad DESC NULLS LAST, esperando_desde DESC NULLS LAST, reference_number
            OFFSET @offset LIMIT @limit
            """;

        var all = parameters
            .Append(("offset", (object)((query.Page - 1) * query.PageSize)))
            .Append(("limit", (object)query.PageSize))
            .ToList();

        var entries = new List<QuipuxBandejaEntry>(query.PageSize);

        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            Prepare(cmd, sql, all);
            var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    entries.Add(Map(reader));
                }
            }
        }

        return entries;
    }

    private static QuipuxBandejaEntry Map(DbDataReader r) => new()
    {
        ProcedureInstanceId = r.GetGuid(r.GetOrdinal("procedure_instance_id")),
        ReferenceNumber = r.GetString(r.GetOrdinal("reference_number")),
        Plate = GetNullableString(r, "placa"),
        ProcedureTypeName = r.GetString(r.GetOrdinal("tipo")),
        Estado = r.GetString(r.GetOrdinal("estado")),
        ClientTenantId = r.GetGuid(r.GetOrdinal("tenant_id")),
        ClientTenantName = r.GetString(r.GetOrdinal("empresa")),
        TransitOfficeName = r.GetString(r.GetOrdinal("secretaria")),
        DivipoCode = GetNullableString(r, "divipo_code"),
        DocumentoQx = GetNullableString(r, "documento_qx"),
        SubmissionId = GetNullableGuid(r, "submission_id"),
        Intentos = r.GetInt32(r.GetOrdinal("intentos")),
        Attempts = r.GetInt32(r.GetOrdinal("attempts")),
        PollCount = r.GetInt32(r.GetOrdinal("poll_count")),
        QxRegisterCode = GetNullableInt(r, "qx_register_code"),
        QxProcedureCode = GetNullableInt(r, "qx_procedure_code"),
        RejectionReason = GetNullableString(r, "rejection_reason"),
        UltimaActividad = GetNullableDate(r, "ultima_actividad"),
        EsperandoDesde = GetNullableDate(r, "esperando_desde"),
        SubmissionCreatedAt = GetNullableDate(r, "submission_created_at"),
    };

    /// <summary>
    /// Filtros COMBINABLES (AC4): todos se acumulan con <c>AND</c>. Cada uno se salta si viene
    /// vacío, y todos van parametrizados — el SQL nunca se concatena con valores del usuario.
    /// </summary>
    private static (string Where, IReadOnlyList<(string, object)> Parameters) BuildFilter(
        QuipuxBandejaQuery q)
    {
        var conditions = new List<string>();
        var parameters = new List<(string, object)>
        {
            ("estado_preparado", TramiteEstado.Preparado),
        };

        if (q.Desde is { } desde)
        {
            // Un sin_radicar no tiene actividad: se acota por su espera, o quedaría siempre fuera.
            conditions.Add("COALESCE(ultima_actividad, esperando_desde) >= @desde");
            parameters.Add(("desde", desde));
        }

        if (q.Hasta is { } hasta)
        {
            conditions.Add("COALESCE(ultima_actividad, esperando_desde) <= @hasta");
            parameters.Add(("hasta", hasta));
        }

        if (Trim(q.Placa) is { } placa)
        {
            conditions.Add("UPPER(placa) = @placa");
            parameters.Add(("placa", placa.ToUpperInvariant()));
        }

        if (q.ProcedureInstanceId is { } instanceId)
        {
            conditions.Add("procedure_instance_id = @instance_id");
            parameters.Add(("instance_id", instanceId));
        }

        if (Trim(q.ReferenceNumber) is { } reference)
        {
            conditions.Add("UPPER(reference_number) LIKE @reference");
            parameters.Add(("reference", $"%{reference.ToUpperInvariant()}%"));
        }

        if (Trim(q.DocumentoQx) is { } documento)
        {
            // Coincidencia PARCIAL a propósito (AC5): el nombre completo
            // (TESLA_MI_20260811_1220_LRWYGCFJ3TC767907) es impracticable de dictar, pero lleva
            // dentro la placa o el VIN, que es justamente lo que soporte tiene a mano.
            conditions.Add("UPPER(documento_qx) LIKE @documento");
            parameters.Add(("documento", $"%{documento.ToUpperInvariant()}%"));
        }

        if (QuipuxBandejaEstados.EsValido(Trim(q.Estado)))
        {
            conditions.Add("estado = @estado");
            parameters.Add(("estado", q.Estado!.Trim()));
        }

        if (q.TransitOfficeId is { } officeId)
        {
            conditions.Add("transit_office_id = @office_id");
            parameters.Add(("office_id", officeId));
        }

        if (q.TenantId is { } tenantId)
        {
            conditions.Add("tenant_id = @tenant_id");
            parameters.Add(("tenant_id", tenantId));
        }

        if (q.ProcedureTypeId is { } typeId)
        {
            conditions.Add("procedure_type_id = @type_id");
            parameters.Add(("type_id", typeId));
        }

        var sb = new StringBuilder(", filtradas AS (SELECT * FROM filas");
        if (conditions.Count > 0)
        {
            sb.Append(" WHERE ").AppendJoin(" AND ", conditions);
        }

        sb.Append(") ");
        return (sb.ToString(), parameters);
    }

    private static void Prepare(
        DbCommand cmd, string sql, IReadOnlyList<(string Name, object Value)> parameters)
    {
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value;
            cmd.Parameters.Add(p);
        }
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? GetNullableString(DbDataReader r, string column)
    {
        var i = r.GetOrdinal(column);
        return r.IsDBNull(i) ? null : r.GetString(i);
    }

    private static Guid? GetNullableGuid(DbDataReader r, string column)
    {
        var i = r.GetOrdinal(column);
        return r.IsDBNull(i) ? null : r.GetGuid(i);
    }

    private static int? GetNullableInt(DbDataReader r, string column)
    {
        var i = r.GetOrdinal(column);
        return r.IsDBNull(i) ? null : r.GetInt32(i);
    }

    private static DateTimeOffset? GetNullableDate(DbDataReader r, string column)
    {
        var i = r.GetOrdinal(column);
        if (r.IsDBNull(i))
        {
            return null;
        }

        var value = r.GetFieldValue<object>(i);
        return value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(dt.ToUniversalTime(), TimeSpan.Zero),
            _ => DateTimeOffset.Parse(value.ToString()!, CultureInfo.InvariantCulture),
        };
    }
}
