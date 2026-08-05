using System.Data.Common;
using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Queries.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Lectura de métricas Reportes 2.0 (HU-B) sobre las tablas operacionales, con SQL crudo y el
/// patrón de <see cref="AnalyticsReadRepository"/>: execution strategy + transacción +
/// <c>set_config('app.current_tenant_id', @tenant, true)</c> (GUC RLS) + filtro explícito
/// <c>tenant_id = @tenant</c> en TODAS las consultas. No hay vista global: el tenant siempre
/// viene resuelto. Cada endpoint ejecuta UN comando multi-statement (una sola ronda al servidor);
/// las fechas de negocio se comparan en hora America/Bogota (§0 del contrato).
/// </summary>
internal sealed class AnalyticsMetricsReadRepository : IAnalyticsMetricsReadRepository
{
    // ── Fragmentos comunes ────────────────────────────────────────────────────────────────────────
    // CTE de instancias del tenant con los filtros comunes §4.1. Los cast ::uuid/::text permiten
    // parámetros NULL tipados (mismo truco que DetailsFilter en AnalyticsReadRepository). El filtro
    // `reason` selecciona instancias con AL MENOS una transición a 'rechazado' cuya causal matchea.
    private const string InstancesCte = """
        WITH insts AS (
            SELECT pi.id, pi.reference_number, pi.status, pi.created_at,
                   pi.transit_office_id, pi.procedure_type_id, pi.created_by_user_id
            FROM tramites.procedure_instances pi
            WHERE pi.tenant_id = @tenant
              AND pi.deleted_at IS NULL
              AND (@office::uuid IS NULL OR pi.transit_office_id = @office::uuid)
              AND (@ptype::uuid IS NULL OR pi.procedure_type_id = @ptype::uuid)
              AND (@operator::uuid IS NULL OR pi.created_by_user_id = @operator::uuid)
              AND (@status::text IS NULL OR pi.status = @status::text)
              AND (@reason::text IS NULL OR EXISTS (
                    SELECT 1 FROM tramites.procedure_instance_status_history hr
                    WHERE hr.tenant_id = @tenant
                      AND hr.procedure_instance_id = pi.id
                      AND hr.to_status = 'rechazado'
                      AND hr.reason ILIKE '%' || @reason::text || '%'))
        )
        """;

    // Rango de negocio sobre transiciones (día calendario Bogotá).
    private const string ChangedInRange =
        "(h.changed_at AT TIME ZONE 'America/Bogota')::date BETWEEN @from AND @to";

    // Tiempos de decisión (§4.2): horas entre la ÚLTIMA transición a 'entregado' y la siguiente
    // transición a 'aprobado'/'rechazado' de la misma instancia, decidida dentro del rango.
    private const string DecidedCte = InstancesCte + """
        , decided AS (
            SELECT i.transit_office_id,
                   EXTRACT(EPOCH FROM (h.changed_at - d.delivered_at)) / 3600.0 AS hours
            FROM tramites.procedure_instance_status_history h
            JOIN insts i ON i.id = h.procedure_instance_id
            LEFT JOIN LATERAL (
                SELECT max(h2.changed_at) AS delivered_at
                FROM tramites.procedure_instance_status_history h2
                WHERE h2.tenant_id = @tenant
                  AND h2.procedure_instance_id = h.procedure_instance_id
                  AND h2.to_status = 'entregado'
                  AND h2.changed_at <= h.changed_at
            ) d ON TRUE
            WHERE h.tenant_id = @tenant
              AND h.to_status IN ('aprobado', 'rechazado')
              AND (h.changed_at AT TIME ZONE 'America/Bogota')::date BETWEEN @from AND @to
              AND d.delivered_at IS NOT NULL
        )
        """;

    // Atascados (§4.2): estado actual NO final ('aprobado'/'anulado') y más de @stuckDays días
    // desde la última transición (o desde la creación si no hay transiciones). Sin rango from/to:
    // describe el estado ACTUAL del universo filtrado.
    private const string StuckCte = InstancesCte + """
        , stuck AS (
            SELECT i.id, i.reference_number, i.status,
                   EXTRACT(EPOCH FROM (now() - coalesce(lh.last_changed_at, i.created_at))) / 86400.0 AS days_in_status,
                   i.transit_office_id, i.procedure_type_id, i.created_by_user_id
            FROM insts i
            LEFT JOIN LATERAL (
                SELECT max(h.changed_at) AS last_changed_at
                FROM tramites.procedure_instance_status_history h
                WHERE h.tenant_id = @tenant AND h.procedure_instance_id = i.id
            ) lh ON TRUE
            WHERE i.status NOT IN ('aprobado', 'anulado')
              AND coalesce(lh.last_changed_at, i.created_at) < now() - make_interval(days => @stuckDays)
        )
        """;

    // ── OT metrics (§4.2): UN comando con 9 statements (una sola ronda) ──────────────────────────
    private const string OtMetricsSql =
        // 1) Summary: transiciones a cada estado dentro del rango.
        InstancesCte + $"""
        SELECT count(*) FILTER (WHERE h.to_status = 'entregado')::int,
               count(*) FILTER (WHERE h.to_status = 'aprobado')::int,
               count(*) FILTER (WHERE h.to_status = 'rechazado')::int
        FROM tramites.procedure_instance_status_history h
        JOIN insts i ON i.id = h.procedure_instance_id
        WHERE h.tenant_id = @tenant AND {ChangedInRange};
        """ +
        // 2) Tiempos de decisión globales (avg/p50/p90 horas).
        DecidedCte + """
        SELECT avg(hours)::float8,
               (percentile_cont(0.5) WITHIN GROUP (ORDER BY hours))::float8,
               (percentile_cont(0.9) WITHIN GROUP (ORDER BY hours))::float8
        FROM decided;
        """ +
        // 3) Reincidencia: rechazadas en rango; reintentadas = el rechazo NO cerró la historia;
        //    ciclos = veces que la instancia pasó por 'rechazado'.
        //
        //    CORRECCIÓN: antes solo contaba la transición rechazado→borrador, que es el camino que
        //    casi nadie recorre. El flujo de subsanación (HU #10871) re-radica rechazado→ENTREGADO
        //    con el flag `subsanacion_activa`, y activar ese flag NO escribe fila de historial a
        //    propósito (StartSubsanacionCommand, para no ensuciar el timeline). Resultado: el
        //    retrabajo real quedaba fuera del conteo. Ahora se cuenta como reintento:
        //      · rechazado → borrador  (reapertura clásica),
        //      · rechazado → entregado (re-radicación tras subsanar),
        //      · subsanacion_count > 0 (subsanación abierta que aún no se re-radicó: el reintento
        //        está en curso, y no contarlo volvería a reportar de menos).
        InstancesCte + $"""
        , rejected AS (
            SELECT DISTINCT h.procedure_instance_id
            FROM tramites.procedure_instance_status_history h
            JOIN insts i ON i.id = h.procedure_instance_id
            WHERE h.tenant_id = @tenant AND h.to_status = 'rechazado' AND {ChangedInRange}
        ),
        cycles AS (
            SELECT (SELECT count(*)
                    FROM tramites.procedure_instance_status_history h
                    WHERE h.tenant_id = @tenant
                      AND h.procedure_instance_id = r.procedure_instance_id
                      AND h.to_status = 'rechazado') AS ciclos,
                   (EXISTS (SELECT 1
                    FROM tramites.procedure_instance_status_history h
                    WHERE h.tenant_id = @tenant
                      AND h.procedure_instance_id = r.procedure_instance_id
                      AND h.from_status = 'rechazado'
                      AND h.to_status IN ('borrador', 'entregado'))
                   OR EXISTS (SELECT 1
                    FROM tramites.procedure_instances pi
                    WHERE pi.tenant_id = @tenant
                      AND pi.id = r.procedure_instance_id
                      AND coalesce(pi.subsanacion_count, 0) > 0)) AS retried
            FROM rejected r
        )
        SELECT count(*)::int,
               count(*) FILTER (WHERE retried)::int,
               coalesce(avg(ciclos), 0)::float8,
               coalesce(max(ciclos), 0)::int
        FROM cycles;
        """ +
        // 4) Rechazo por OT.
        InstancesCte + $"""
        SELECT i.transit_office_id, tofc.name,
               count(*) FILTER (WHERE h.to_status = 'entregado')::int,
               count(*) FILTER (WHERE h.to_status = 'aprobado')::int,
               count(*) FILTER (WHERE h.to_status = 'rechazado')::int
        FROM tramites.procedure_instance_status_history h
        JOIN insts i ON i.id = h.procedure_instance_id
        JOIN catalogs.transit_offices tofc ON tofc.id = i.transit_office_id
        WHERE h.tenant_id = @tenant AND {ChangedInRange}
        GROUP BY i.transit_office_id, tofc.name
        ORDER BY 5 DESC, 2 ASC;
        """ +
        // 5) Causales de rechazo (texto libre; se agrupa la causal recortada, vacía → 'Sin causal').
        InstancesCte + $"""
        SELECT coalesce(nullif(btrim(h.reason), ''), 'Sin causal') AS causal, count(*)::int
        FROM tramites.procedure_instance_status_history h
        JOIN insts i ON i.id = h.procedure_instance_id
        WHERE h.tenant_id = @tenant AND h.to_status = 'rechazado' AND {ChangedInRange}
          AND (@reason::text IS NULL OR h.reason ILIKE '%' || @reason::text || '%')
        GROUP BY 1
        ORDER BY 2 DESC, 1 ASC;
        """ +
        // 6) Rechazo por tipo de trámite.
        InstancesCte + $"""
        SELECT i.procedure_type_id, pt.name,
               count(*) FILTER (WHERE h.to_status = 'entregado')::int,
               count(*) FILTER (WHERE h.to_status = 'rechazado')::int
        FROM tramites.procedure_instance_status_history h
        JOIN insts i ON i.id = h.procedure_instance_id
        JOIN tramites.procedure_types pt ON pt.id = i.procedure_type_id
        WHERE h.tenant_id = @tenant AND {ChangedInRange}
        GROUP BY i.procedure_type_id, pt.name
        ORDER BY 4 DESC, 2 ASC;
        """ +
        // 7) Tiempos de decisión por OT.
        DecidedCte + """
        SELECT d.transit_office_id, tofc.name,
               count(*)::int AS decididos,
               avg(d.hours)::float8,
               (percentile_cont(0.5) WITHIN GROUP (ORDER BY d.hours))::float8,
               (percentile_cont(0.9) WITHIN GROUP (ORDER BY d.hours))::float8
        FROM decided d
        JOIN catalogs.transit_offices tofc ON tofc.id = d.transit_office_id
        GROUP BY d.transit_office_id, tofc.name
        ORDER BY 5 ASC NULLS LAST, 2 ASC;
        """ +
        // 8) Atascados: total del universo filtrado.
        StuckCte + """
        SELECT count(*)::int FROM stuck;
        """ +
        // 9) Atascados: top 50 por días en el estado.
        StuckCte + """
        SELECT s.id, s.reference_number, s.status, s.days_in_status::float8,
               tofc.name, pt.name, u.display_name
        FROM stuck s
        LEFT JOIN catalogs.transit_offices tofc ON tofc.id = s.transit_office_id
        JOIN tramites.procedure_types pt ON pt.id = s.procedure_type_id
        JOIN identity.users u ON u.id = s.created_by_user_id
        ORDER BY s.days_in_status DESC, s.id ASC
        LIMIT 50;
        """ +
        // 10) Causales TIPIFICADAS del catálogo. Convive con el statement 5 (texto libre): mientras
        //     queden rechazos anteriores a las causales, ambos aportan. El conteo es de RECHAZOS
        //     distintos que incluyen la causal, no de marcas — un rechazo con tres causales sigue
        //     siendo un rechazo, y por eso los porcentajes pueden sumar más de 100 %.
        InstancesCte + $"""
        SELECT rr.id, rr.code, rr.description,
               count(DISTINCT pirr.status_history_id)::int AS rechazos
        FROM tramites.procedure_instance_rejection_reasons pirr
        JOIN insts i ON i.id = pirr.procedure_instance_id
        JOIN tramites.procedure_instance_status_history h ON h.id = pirr.status_history_id
        JOIN catalogs.rejection_reasons rr ON rr.id = pirr.rejection_reason_id
        WHERE pirr.tenant_id = @tenant
          AND h.tenant_id = @tenant
          AND h.to_status = 'rechazado' AND {ChangedInRange}
        GROUP BY rr.id, rr.code, rr.description
        ORDER BY 4 DESC, 3 ASC;
        """ +
        // 11) Ciclo INTERNO: horas desde que se creó el trámite hasta que se entregó por primera vez.
        //     Es el tramo propio de la empresa. Hasta ahora solo se medía el tramo del organismo
        //     (entregado→decisión), así que se podía señalar al lento pero no corregirse uno mismo.
        InstancesCte + """
        , first_delivery AS (
            SELECT i.id,
                   i.created_at,
                   min(h.changed_at) AS delivered_at
            FROM insts i
            JOIN tramites.procedure_instance_status_history h
              ON h.procedure_instance_id = i.id
             AND h.tenant_id = @tenant
             AND h.to_status = 'entregado'
            GROUP BY i.id, i.created_at
        ), internal_hours AS (
            SELECT EXTRACT(EPOCH FROM (delivered_at - created_at)) / 3600.0 AS hours
            FROM first_delivery
            WHERE (delivered_at AT TIME ZONE 'America/Bogota')::date BETWEEN @from AND @to
        )
        SELECT avg(hours)::float8,
               (percentile_cont(0.5) WITHIN GROUP (ORDER BY hours))::float8,
               (percentile_cont(0.9) WITHIN GROUP (ORDER BY hours))::float8
        FROM internal_hours;
        """;

    // ── Funnel (§4.3): cohorte por created_at (Bogotá) de la instancia dentro del rango ───────────
    // "Alcanzó la etapa" = tiene transición a ese estado o su estado actual es ese estado
    // (el nacimiento en 'borrador' no siempre deja fila en history → borrador = toda la cohorte).
    private const string FunnelSql = InstancesCte + """
        SELECT count(*)::int AS borrador,
               count(*) FILTER (WHERE i.status = 'preparado' OR EXISTS (
                   SELECT 1 FROM tramites.procedure_instance_status_history h
                   WHERE h.tenant_id = @tenant AND h.procedure_instance_id = i.id
                     AND h.to_status = 'preparado'))::int AS preparado,
               count(*) FILTER (WHERE i.status = 'entregado' OR EXISTS (
                   SELECT 1 FROM tramites.procedure_instance_status_history h
                   WHERE h.tenant_id = @tenant AND h.procedure_instance_id = i.id
                     AND h.to_status = 'entregado'))::int AS entregado,
               count(*) FILTER (WHERE i.status = 'aprobado' OR EXISTS (
                   SELECT 1 FROM tramites.procedure_instance_status_history h
                   WHERE h.tenant_id = @tenant AND h.procedure_instance_id = i.id
                     AND h.to_status = 'aprobado'))::int AS aprobado,
               count(*) FILTER (WHERE i.status = 'anulado')::int AS anulados,
               count(*) FILTER (WHERE i.status = 'rechazado')::int AS rechazados_vigentes
        FROM insts i
        WHERE (i.created_at AT TIME ZONE 'America/Bogota')::date BETWEEN @from AND @to;
        """;

    // ── Reemplazos de documentos (§4.4, regla §0) ─────────────────────────────────────────────────
    // "Reemplazo" = fila que NO es la primera de su (instancia, tipo) por uploaded_at (desempate id).
    private const string DocumentReplacementsSql = InstancesCte + """
        SELECT a.tipo,
               count(*)::int AS uploads,
               count(*) FILTER (WHERE EXISTS (
                   SELECT 1 FROM tramites.procedure_instance_attachments a2
                   WHERE a2.tenant_id = @tenant
                     AND a2.procedure_instance_id = a.procedure_instance_id
                     AND a2.tipo = a.tipo
                     AND (a2.uploaded_at < a.uploaded_at
                          OR (a2.uploaded_at = a.uploaded_at AND a2.id < a.id))))::int AS replacements
        FROM tramites.procedure_instance_attachments a
        JOIN insts i ON i.id = a.procedure_instance_id
        WHERE a.tenant_id = @tenant
          AND (a.uploaded_at AT TIME ZONE 'America/Bogota')::date BETWEEN @from AND @to
        GROUP BY a.tipo
        ORDER BY 3 DESC, 2 DESC, 1 ASC;
        """;

    // ── Integraciones externas (§4.4): errors = response_code >= 400 OR error_message IS NOT NULL ─
    private const string ExternalApisSql = """
        SELECT l.endpoint, l.direction,
               count(*)::int AS calls,
               count(*) FILTER (WHERE l.response_code >= 400 OR l.error_message IS NOT NULL)::int AS errors,
               avg(l.duration_ms)::float8,
               (percentile_cont(0.9) WITHIN GROUP (ORDER BY l.duration_ms::float8))::float8
        FROM admin.ot_api_call_logs l
        WHERE l.tenant_id = @tenant
          AND (l.called_at AT TIME ZONE 'America/Bogota')::date BETWEEN @from AND @to
        GROUP BY l.endpoint, l.direction
        ORDER BY 3 DESC, 1 ASC, 2 ASC;
        """;

    // ── Live overview (§4.5): UN comando con 7 statements — una sola conexión/ronda, < 300 ms ────
    private const string LiveOverviewSql = """
        SELECT count(*)::int
        FROM tramites.procedure_instances pi
        WHERE pi.tenant_id = @tenant AND pi.deleted_at IS NULL
          AND (pi.created_at AT TIME ZONE 'America/Bogota')::date = (now() AT TIME ZONE 'America/Bogota')::date;

        SELECT pi.status, count(*)::int
        FROM tramites.procedure_instances pi
        WHERE pi.tenant_id = @tenant AND pi.deleted_at IS NULL
          AND pi.status NOT IN ('aprobado', 'anulado')
        GROUP BY pi.status
        ORDER BY pi.status;

        SELECT count(*) FILTER (WHERE h.to_status = 'entregado')::int,
               count(*) FILTER (WHERE h.to_status = 'aprobado')::int,
               count(*) FILTER (WHERE h.to_status = 'rechazado')::int
        FROM tramites.procedure_instance_status_history h
        WHERE h.tenant_id = @tenant
          AND (h.changed_at AT TIME ZONE 'America/Bogota')::date = (now() AT TIME ZONE 'America/Bogota')::date;

        SELECT count(*)::int
        FROM tramites.procedure_instances pi
        LEFT JOIN LATERAL (
            SELECT max(h.changed_at) AS last_changed_at
            FROM tramites.procedure_instance_status_history h
            WHERE h.tenant_id = @tenant AND h.procedure_instance_id = pi.id
        ) lh ON TRUE
        WHERE pi.tenant_id = @tenant AND pi.deleted_at IS NULL
          AND pi.status NOT IN ('aprobado', 'anulado')
          AND coalesce(lh.last_changed_at, pi.created_at) < now() - make_interval(days => @stuckDays);

        SELECT count(*)::int
        FROM tramites.procedure_instance_biometric_validations v
        WHERE v.tenant_id = @tenant
          AND v.status IN ('enviado', 'en_proceso', 'pendiente_envio');

        SELECT count(*)::int,
               count(*) FILTER (WHERE l.response_code >= 400 OR l.error_message IS NOT NULL)::int,
               avg(l.duration_ms)::float8
        FROM admin.ot_api_call_logs l
        WHERE l.tenant_id = @tenant AND l.called_at >= now() - interval '1 hour';

        SELECT greatest(
            (SELECT max(h.changed_at) FROM tramites.procedure_instance_status_history h WHERE h.tenant_id = @tenant),
            (SELECT max(pi.created_at) FROM tramites.procedure_instances pi WHERE pi.tenant_id = @tenant AND pi.deleted_at IS NULL),
            (SELECT max(l.called_at) FROM admin.ot_api_call_logs l WHERE l.tenant_id = @tenant));
        """;

    private readonly FlitDbContext _context;

    public AnalyticsMetricsReadRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    // ── IAnalyticsMetricsReadRepository ──────────────────────────────────────────────────────────

    public Task<OtMetricsDto> GetOtMetricsAsync(MetricsFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return ExecuteWithTenantAsync(filter.TenantId, async (conn, tx) =>
        {
            await using var cmd = CreateCommand(conn, tx, OtMetricsSql);
            AddCommonFilterParams(cmd, filter);
            AddParam(cmd, "stuckDays", filter.StuckDays);

            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

            // 1) Summary: transiciones del rango.
            await reader.ReadAsync(ct).ConfigureAwait(false);
            var entregados = reader.GetInt32(0);
            var aprobados = reader.GetInt32(1);
            var rechazados = reader.GetInt32(2);

            // 2) Tiempos de decisión globales.
            await reader.NextResultAsync(ct).ConfigureAwait(false);
            await reader.ReadAsync(ct).ConfigureAwait(false);
            var avgHours = GetNullableDouble(reader, 0);
            var p50Hours = GetNullableDouble(reader, 1);
            var p90Hours = GetNullableDouble(reader, 2);

            // 3) Reincidencia.
            await reader.NextResultAsync(ct).ConfigureAwait(false);
            await reader.ReadAsync(ct).ConfigureAwait(false);
            var reincidence = new ReincidenceDto(
                reader.GetInt32(0), reader.GetInt32(1), Round1(reader.GetDouble(2)), reader.GetInt32(3));

            // 4) Rechazo por OT.
            await reader.NextResultAsync(ct).ConfigureAwait(false);
            var rejectionByOffice = new List<RejectionByOfficeDto>();
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var e = reader.GetInt32(2);
                var a = reader.GetInt32(3);
                var r = reader.GetInt32(4);
                rejectionByOffice.Add(new RejectionByOfficeDto(
                    reader.GetGuid(0), reader.GetString(1), e, a, r, Pct(r, a + r)));
            }

            // 5) Causales de rechazo.
            await reader.NextResultAsync(ct).ConfigureAwait(false);
            var reasonRows = new List<(string Reason, int Count)>();
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                reasonRows.Add((reader.GetString(0), reader.GetInt32(1)));
            var reasonTotal = reasonRows.Sum(r => r.Count);
            var rejectionByReason = reasonRows
                .Select(r => new RejectionByReasonDto(r.Reason, r.Count, Pct(r.Count, reasonTotal)))
                .ToList();

            // 6) Rechazo por tipo.
            await reader.NextResultAsync(ct).ConfigureAwait(false);
            var rejectionByType = new List<RejectionByTypeDto>();
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var e = reader.GetInt32(2);
                var r = reader.GetInt32(3);
                rejectionByType.Add(new RejectionByTypeDto(
                    reader.GetGuid(0), reader.GetString(1), e, r, Pct(r, e + r)));
            }

            // 7) Tiempos por OT.
            await reader.NextResultAsync(ct).ConfigureAwait(false);
            var approvalTimesByOffice = new List<ApprovalTimesByOfficeDto>();
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                approvalTimesByOffice.Add(new ApprovalTimesByOfficeDto(
                    reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2),
                    Round1(GetNullableDouble(reader, 3)),
                    Round1(GetNullableDouble(reader, 4)),
                    Round1(GetNullableDouble(reader, 5))));
            }

            // 8) Atascados: total.
            await reader.NextResultAsync(ct).ConfigureAwait(false);
            await reader.ReadAsync(ct).ConfigureAwait(false);
            var stuckTotal = reader.GetInt32(0);

            // 9) Atascados: top 50.
            await reader.NextResultAsync(ct).ConfigureAwait(false);
            var stuckItems = new List<StuckItemDto>();
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                stuckItems.Add(new StuckItemDto(
                    reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                    Round1(reader.GetDouble(3)),
                    await reader.IsDBNullAsync(4, ct).ConfigureAwait(false) ? null : reader.GetString(4),
                    reader.GetString(5), reader.GetString(6)));
            }

            // Ranking de agilidad (§4.2): p50 asc, desempate por tasa de rechazo asc; solo OTs
            // con ≥ 1 decidido. Volumen = decisiones del rango.
            var rejectionRateByOffice = rejectionByOffice.ToDictionary(o => o.TransitOfficeId, o => o.RejectionRatePct);
            var officeRanking = approvalTimesByOffice
                .Where(o => o is { Decididos: > 0, P50Hours: not null })
                .Select(o => (Office: o, Rate: rejectionRateByOffice.GetValueOrDefault(o.TransitOfficeId)))
                .OrderBy(x => x.Office.P50Hours!.Value)
                .ThenBy(x => x.Rate)
                .ThenBy(x => x.Office.TransitOfficeName, StringComparer.Ordinal)
                .Select((x, index) => new OfficeRankingDto(
                    x.Office.TransitOfficeId, x.Office.TransitOfficeName, index + 1,
                    x.Office.P50Hours!.Value, x.Rate, x.Office.Decididos))
                .ToList();

            // 10) Causales tipificadas del catálogo.
            await reader.NextResultAsync(ct).ConfigureAwait(false);
            var rejectionByReasonCatalog = new List<RejectionByReasonCatalogDto>();
            var totalReasonMarks = 0;
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var rechazosConLaCausal = reader.GetInt32(3);
                totalReasonMarks += rechazosConLaCausal;
                rejectionByReasonCatalog.Add(new RejectionByReasonCatalogDto(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    rechazosConLaCausal,
                    // Sobre RECHAZOS, no sobre marcas: un rechazo con tres causales sigue siendo uno.
                    // Por eso la columna puede sumar más de 100 % y así se rotula en el frontend.
                    Pct(rechazosConLaCausal, rechazados)));
            }

            // 11) Ciclo interno (creación → primera entrega).
            await reader.NextResultAsync(ct).ConfigureAwait(false);
            await reader.ReadAsync(ct).ConfigureAwait(false);
            var internalCycle = new InternalCycleDto(
                Round1(GetNullableDouble(reader, 0)),
                Round1(GetNullableDouble(reader, 1)),
                Round1(GetNullableDouble(reader, 2)));

            var summary = new OtMetricsSummaryDto(
                entregados, aprobados, rechazados,
                Pct(rechazados, aprobados + rechazados),
                Round1(avgHours), Round1(p50Hours), Round1(p90Hours),
                Pct(reincidence.Reintentadas, reincidence.Rechazadas),
                stuckTotal);

            return new OtMetricsDto(
                summary, rejectionByOffice, rejectionByReason, rejectionByType,
                approvalTimesByOffice, officeRanking, reincidence,
                new StuckDto(stuckTotal, stuckItems),
                rejectionByReasonCatalog,
                rechazados == 0 ? 0 : Math.Round(totalReasonMarks / (double)rechazados, 2),
                internalCycle);
        }, ct);
    }

    public Task<FunnelCoreDto> GetFunnelAsync(MetricsFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return ExecuteWithTenantAsync(filter.TenantId, async (conn, tx) =>
        {
            await using var cmd = CreateCommand(conn, tx, FunnelSql);
            AddCommonFilterParams(cmd, filter);

            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await reader.ReadAsync(ct).ConfigureAwait(false);

            var counts = new[] { reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3) };
            var anulados = reader.GetInt32(4);
            var rechazadosVigentes = reader.GetInt32(5);

            string[] stages = ["borrador", "preparado", "entregado", "aprobado"];
            var first = counts[0];
            var states = new List<FunnelStageDto>(stages.Length);
            for (var s = 0; s < stages.Length; s++)
            {
                var prev = s == 0 ? counts[0] : counts[s - 1];
                states.Add(new FunnelStageDto(
                    stages[s], counts[s],
                    Pct(counts[s], first),
                    s == 0 ? (first > 0 ? 100.0 : 0.0) : Pct(counts[s], prev)));
            }

            return new FunnelCoreDto(states, anulados, rechazadosVigentes);
        }, ct);
    }

    public Task<IReadOnlyList<DocumentReplacementDto>> GetDocumentReplacementsAsync(
        MetricsFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return ExecuteWithTenantAsync<IReadOnlyList<DocumentReplacementDto>>(filter.TenantId, async (conn, tx) =>
        {
            await using var cmd = CreateCommand(conn, tx, DocumentReplacementsSql);
            AddCommonFilterParams(cmd, filter);

            var items = new List<DocumentReplacementDto>();
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                items.Add(new DocumentReplacementDto(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2)));
            return items;
        }, ct);
    }

    public Task<IReadOnlyList<ExternalApiMetricDto>> GetExternalApiMetricsAsync(
        MetricsFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return ExecuteWithTenantAsync<IReadOnlyList<ExternalApiMetricDto>>(filter.TenantId, async (conn, tx) =>
        {
            await using var cmd = CreateCommand(conn, tx, ExternalApisSql);
            AddParam(cmd, "tenant", filter.TenantId);
            AddParam(cmd, "from", filter.From);
            AddParam(cmd, "to", filter.To);

            var items = new List<ExternalApiMetricDto>();
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var calls = reader.GetInt32(2);
                var errors = reader.GetInt32(3);
                items.Add(new ExternalApiMetricDto(
                    reader.GetString(0), reader.GetString(1), calls, errors,
                    Pct(errors, calls),
                    Round1(GetNullableDouble(reader, 4)),
                    Round1(GetNullableDouble(reader, 5))));
            }

            return items;
        }, ct);
    }

    public Task<LiveOverviewDataDto> GetLiveOverviewAsync(
        Guid tenantId, int stuckDays, CancellationToken ct = default) =>
        ExecuteWithTenantAsync(tenantId, async (conn, tx) =>
        {
            await using var cmd = CreateCommand(conn, tx, LiveOverviewSql);
            AddParam(cmd, "tenant", tenantId);
            AddParam(cmd, "stuckDays", stuckDays);

            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

            // 1) Creados hoy (día Bogotá).
            await reader.ReadAsync(ct).ConfigureAwait(false);
            var creados = reader.GetInt32(0);

            // 2) Estado actual de instancias activas (no finales).
            await reader.NextResultAsync(ct).ConfigureAwait(false);
            var byStatus = new List<StatusCountItemDto>();
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                byStatus.Add(new StatusCountItemDto(reader.GetString(0), reader.GetInt32(1)));

            // 3) Transiciones de HOY.
            await reader.NextResultAsync(ct).ConfigureAwait(false);
            await reader.ReadAsync(ct).ConfigureAwait(false);
            var today = new LiveTodayDto(
                creados, byStatus, reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));

            // 4) Atascados.
            await reader.NextResultAsync(ct).ConfigureAwait(false);
            await reader.ReadAsync(ct).ConfigureAwait(false);
            var stuckCount = reader.GetInt32(0);

            // 5) Validaciones de identidad pendientes / en proceso.
            await reader.NextResultAsync(ct).ConfigureAwait(false);
            await reader.ReadAsync(ct).ConfigureAwait(false);
            var pendingValidations = reader.GetInt32(0);

            // 6) Integraciones de la última hora.
            await reader.NextResultAsync(ct).ConfigureAwait(false);
            await reader.ReadAsync(ct).ConfigureAwait(false);
            var integrations = new IntegrationsLastHourDto(
                reader.GetInt32(0), reader.GetInt32(1), Round1(GetNullableDouble(reader, 2)));

            // 7) Última actividad (cambio de estado, creación o llamada externa).
            await reader.NextResultAsync(ct).ConfigureAwait(false);
            await reader.ReadAsync(ct).ConfigureAwait(false);
            DateTimeOffset? lastActivityAt = await reader.IsDBNullAsync(0, ct).ConfigureAwait(false)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(0);

            return new LiveOverviewDataDto(today, stuckCount, pendingValidations, integrations, lastActivityAt);
        }, ct);

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Parámetros de los filtros comunes §4.1 (los NULL viajan tipados via cast en el SQL).</summary>
    private static void AddCommonFilterParams(DbCommand cmd, MetricsFilter filter)
    {
        AddParam(cmd, "tenant", filter.TenantId);
        AddParam(cmd, "from", filter.From);
        AddParam(cmd, "to", filter.To);
        AddParam(cmd, "office", (object?)filter.TransitOfficeId ?? DBNull.Value);
        AddParam(cmd, "ptype", (object?)filter.ProcedureTypeId ?? DBNull.Value);
        AddParam(cmd, "operator", (object?)filter.OperatorUserId ?? DBNull.Value);
        AddParam(cmd, "status", (object?)filter.Status ?? DBNull.Value);
        AddParam(cmd, "reason", (object?)filter.Reason ?? DBNull.Value);
    }

    private static double? GetNullableDouble(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);

    /// <summary>Porcentaje redondeado a 1 decimal; 0 cuando el denominador es 0.</summary>
    private static double Pct(int part, int total) =>
        total == 0 ? 0.0 : Math.Round(part * 100.0 / total, 1, MidpointRounding.AwayFromZero);

    private static double Round1(double value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);

    private static double? Round1(double? value) =>
        value is { } v ? Math.Round(v, 1, MidpointRounding.AwayFromZero) : null;

    /// <summary>
    /// Abre una transacción reintentablemente, fija el GUC de tenant para RLS y ejecuta la consulta
    /// (patrón de <see cref="AnalyticsReadRepository"/>). El GUC es local a la transacción: se
    /// revierte al cerrar y no contamina la conexión del pool.
    /// </summary>
    private async Task<T> ExecuteWithTenantAsync<T>(
        Guid tenantId, Func<DbConnection, DbTransaction, Task<T>> body, CancellationToken ct)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var transaction = await _context.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)", ct)
                    .ConfigureAwait(false);

                var conn = _context.Database.GetDbConnection();
                var tx = transaction.GetDbTransaction();
                var result = await body(conn, tx).ConfigureAwait(false);

                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return result;
            }
        }).ConfigureAwait(false);
    }

    private static DbCommand CreateCommand(DbConnection conn, DbTransaction tx, string sql)
    {
        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        return cmd;
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
