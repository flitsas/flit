using System.Data.Common;
using System.Globalization;
using Flit.Infrastructure.Documents.Reports;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Flit.Infrastructure.Analytics.Scheduling;

/// <summary>
/// Reportes 2.0 (HU-D) — arma el adjunto Excel de los 4 informes programados de alcance ICT
/// (Integración con Terceros): "ict_novedades" (<see cref="BuildNovedadesAsync"/>), "ict_atascados"
/// (<see cref="BuildAtascadosAsync"/>), "ict_jobs" (<see cref="BuildJobsAsync"/>, platform-wide,
/// SuperAdmin-only — la restricción vive en <c>ReportSchedulesEndpoints</c>, no aquí) y
/// "ict_webhooks" (<see cref="BuildWebhooksAsync"/>). Los 4 son SOLO Excel (§ <c>SchedulingValidation</c>),
/// igual que "consulta" — no hay una versión PDF con sentido para un detalle fila a fila.
///
/// <para><b>Cross-schema, no cross-servicio</b>, mismo patrón que <see cref="IctQueryRepository"/>
/// y <see cref="AlertMetricsReadRepository"/>: SQL crudo sobre la conexión de
/// <see cref="FlitDbContext"/> contra <c>ict.*</c> (propiedad de core-ict, mismo Postgres), fijando
/// el GUC <c>app.current_tenant_id</c> por RLS y filtrando además <c>tenant_id</c> explícito. No se
/// reutiliza el <c>WithTenantAsync</c> privado de <see cref="IctQueryRepository"/> — vive en una
/// clase distinta con su propio ciclo de vida de DI; se replica aquí el mismo patrón corto en vez de
/// exponerlo como compartido, que hubiera acoplado dos módulos por una utilidad de 15 líneas.</para>
/// </summary>
internal sealed class IctOwnReportDocumentBuilder(FlitDbContext context)
{
    private static readonly CultureInfo Es = CultureInfo.InvariantCulture;

    /// <summary>Tope de filas de detalle por informe — mismo criterio que
    /// <see cref="OtOwnReportDocumentBuilder"/> (informe automático, no exploración interactiva).</summary>
    private const int MaxRows = 2_000;

    private static readonly (string Causa, string Contiene)[] CausasConocidas =
    [
        ("SOAT", "SOAT"),
        ("RTM", "RTM"),
        ("RNMC", "RNMC"),
        ("Documento faltante", "DOCUMENTO"),
    ];

    // ── "ict_novedades": novedades por causa (segmentado por subcadena en los comentarios) ─────

    public async Task<byte[]> BuildNovedadesAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var (fromUtc, toUtc) = BogotaDays.Range(from, to);
        var rows = new List<(string? Placa, string? Vin, string? Radicado, string? Comentarios, DateTimeOffset CreatedAt)>();

        await WithTenantAsync(tenantId, async (cmd, token) =>
        {
            cmd.CommandText = """
                SELECT plate, vin, manager_id_transaction,
                       business_comments_validation, external_comments_validation, created_at
                FROM ict.external_integration_master
                WHERE tenant_id = @tenant AND deleted_at IS NULL AND process_status_id = 4
                  AND created_at >= @from AND created_at <= @to
                ORDER BY created_at DESC
                LIMIT @limite
                """;
            AddParam(cmd, "tenant", tenantId);
            AddParam(cmd, "from", fromUtc);
            AddParam(cmd, "to", toUtc);
            AddParam(cmd, "limite", MaxRows);

            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var comentarios = IctQueryRepository.CombineComentarios(
                    GetStringOrNull(reader, "business_comments_validation"),
                    GetStringOrNull(reader, "external_comments_validation"));

                rows.Add((
                    GetStringOrNull(reader, "plate"),
                    GetStringOrNull(reader, "vin"),
                    GetStringOrNull(reader, "manager_id_transaction"),
                    comentarios,
                    reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at"))));
            }
        }, ct).ConfigureAwait(false);

        return BuildNovedadesExcel(rows);
    }

    private static byte[] BuildNovedadesExcel(
        List<(string? Placa, string? Vin, string? Radicado, string? Comentarios, DateTimeOffset CreatedAt)> rows)
    {
        // Sin taxonomía de códigos: se clasifica por coincidencia de subcadena (mayúsculas, sin
        // acentos) contra una lista fija de causas conocidas; lo que no matchea cae en "Otra/sin
        // clasificar" — no se descarta, se cuenta igual para que el % del resumen sume el total.
        var porCausa = CausasConocidas.ToDictionary(c => c.Causa, _ => 0, StringComparer.Ordinal);
        var sinClasificar = 0;

        foreach (var row in rows)
        {
            var causa = ClassifyCausa(row.Comentarios);
            if (causa is null)
                sinClasificar++;
            else
                porCausa[causa]++;
        }

        var total = rows.Count;
        var resumenFilas = porCausa
            .Select(kv => (IReadOnlyList<string>)
                [kv.Key, kv.Value.ToString(Es), Pct(kv.Value, total)])
            .Append((IReadOnlyList<string>)["Otra/sin clasificar", sinClasificar.ToString(Es), Pct(sinClasificar, total)])
            .ToList();

        var sheets = new List<TabularWorkbookWriter.Sheet>
        {
            TabularWorkbookWriter.Sheet.OfText(
                "Resumen por causa", ["Causa", "Cantidad", "% del total"], resumenFilas),
            TabularWorkbookWriter.Sheet.OfText(
                rows.Count >= MaxRows ? $"Detalle (top {MaxRows})" : "Detalle",
                ["Placa", "VIN", "Radicado", "Comentarios", "Fecha de registro"],
                rows.Select(r => (IReadOnlyList<string>)
                [
                    r.Placa ?? "", r.Vin ?? "", r.Radicado ?? "", r.Comentarios ?? "",
                    r.CreatedAt.ToString("yyyy-MM-dd HH:mm", Es),
                ]).ToList()),
        };

        return TabularWorkbookWriter.Write(sheets);
    }

    // ── "ict_atascados": pre-trámites que siguen sin borrador HOY (sin filtro de rango) ────────

    public async Task<byte[]> BuildAtascadosAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        // Deliberadamente SIN filtro por from/to: son los que siguen atascados AHORA, no los creados
        // en el periodo — mismo criterio que la métrica de alerta ict_stuck_in_validation
        // (AlertMetricsReadRepository.IctStuckInValidationSql), que tampoco acota por fecha de
        // creación, solo por cuánto tiempo lleva sin resolverse.
        _ = from;
        _ = to;

        var rows = new List<(string? Placa, string? Vin, string? Radicado, bool EsperandoNegocio, DateTimeOffset CreatedAt)>();

        await WithTenantAsync(tenantId, async (cmd, token) =>
        {
            cmd.CommandText = """
                SELECT plate, vin, manager_id_transaction, business_date_validation, created_at
                FROM ict.external_integration_master
                WHERE tenant_id = @tenant AND deleted_at IS NULL
                  AND process_status_id IN (1, 2) AND procedure_instance_id IS NULL
                ORDER BY created_at ASC
                LIMIT @limite
                """;
            AddParam(cmd, "tenant", tenantId);
            AddParam(cmd, "limite", MaxRows);

            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var businessDateValidation = GetDateTimeOrNull(reader, "business_date_validation");
                rows.Add((
                    GetStringOrNull(reader, "plate"),
                    GetStringOrNull(reader, "vin"),
                    GetStringOrNull(reader, "manager_id_transaction"),
                    businessDateValidation is null,
                    reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at"))));
            }
        }, ct).ConfigureAwait(false);

        return BuildAtascadosExcel(rows);
    }

    private static byte[] BuildAtascadosExcel(
        List<(string? Placa, string? Vin, string? Radicado, bool EsperandoNegocio, DateTimeOffset CreatedAt)> rows)
    {
        var hoy = DateTimeOffset.UtcNow;
        var sheets = new List<TabularWorkbookWriter.Sheet>
        {
            TabularWorkbookWriter.Sheet.OfText(
                rows.Count >= MaxRows ? $"Atascados (top {MaxRows})" : "Atascados",
                ["Placa", "VIN", "Radicado", "Esperando", "Días desde el registro"],
                rows.Select(r => (IReadOnlyList<string>)
                [
                    r.Placa ?? "", r.Vin ?? "", r.Radicado ?? "",
                    r.EsperandoNegocio ? "Validación de negocio" : "Fuente externa (RUNT/RNMC/SOAT)",
                    ((hoy - r.CreatedAt).TotalDays).ToString("0.#", Es),
                ]).ToList()),
        };

        return TabularWorkbookWriter.Write(sheets);
    }

    // ── "ict_jobs": rendimiento de los jobs del pipeline (platform-wide, SuperAdmin-only) ──────

    /// <summary>
    /// <c>ict.job_runs</c> es GLOBAL de plataforma (sin <c>tenant_id</c>, sin RLS — ver el DDL
    /// <c>16-ICT-job-runs.sql</c> de core-ict) — <paramref name="tenantId"/> se recibe solo para
    /// mantener la misma firma que los otros 3 builders y no se usa en la consulta. La restricción de
    /// que solo SuperAdmin pueda crear/editar un schedule "ict_jobs" vive en
    /// <c>ReportSchedulesEndpoints</c> (capa de autorización), no aquí.
    /// </summary>
    public async Task<byte[]> BuildJobsAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        _ = tenantId;
        var (fromUtc, toUtc) = BogotaDays.Range(from, to);
        var rows = new List<(string JobName, string Outcome, bool BreachedSla, int DurationMs, DateTimeOffset StartedAt)>();

        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            var conn = context.Database.GetDbConnection();
            var wasClosed = conn.State != System.Data.ConnectionState.Open;
            if (wasClosed)
                await conn.OpenAsync(ct).ConfigureAwait(false);
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT job_name, outcome, breached_sla, duration_ms, started_at
                    FROM ict.job_runs
                    WHERE started_at >= @from AND started_at <= @to
                    ORDER BY started_at DESC
                    LIMIT @limite
                    """;
                AddParam(cmd, "from", fromUtc);
                AddParam(cmd, "to", toUtc);
                AddParam(cmd, "limite", MaxRows);

                await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    rows.Add((
                        reader.GetString(reader.GetOrdinal("job_name")),
                        reader.GetString(reader.GetOrdinal("outcome")),
                        reader.GetBoolean(reader.GetOrdinal("breached_sla")),
                        reader.GetInt32(reader.GetOrdinal("duration_ms")),
                        reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("started_at"))));
                }
            }
            finally
            {
                if (wasClosed)
                    await conn.CloseAsync().ConfigureAwait(false);
            }
        }).ConfigureAwait(false);

        return BuildJobsExcel(rows);
    }

    private static byte[] BuildJobsExcel(
        List<(string JobName, string Outcome, bool BreachedSla, int DurationMs, DateTimeOffset StartedAt)> rows)
    {
        var porJob = rows
            .GroupBy(r => r.JobName, StringComparer.Ordinal)
            .Select(g => (IReadOnlyList<string>)
            [
                g.Key,
                g.Count().ToString(Es),
                (g.Average(r => r.DurationMs) / 1000d).ToString("0.##", Es),
                (g.Max(r => r.DurationMs) / 1000d).ToString("0.##", Es),
                Pct(g.Count(r => r.BreachedSla), g.Count()),
            ])
            .OrderBy(r => r[0], StringComparer.Ordinal)
            .ToList();

        var incumplidas = rows.Where(r => r.BreachedSla)
            .OrderByDescending(r => r.StartedAt)
            .Take(MaxRows)
            .Select(r => (IReadOnlyList<string>)
            [
                r.JobName, r.Outcome, (r.DurationMs / 1000d).ToString("0.##", Es),
                r.StartedAt.ToString("yyyy-MM-dd HH:mm", Es),
            ])
            .ToList();

        var sheets = new List<TabularWorkbookWriter.Sheet>
        {
            TabularWorkbookWriter.Sheet.OfText(
                "Resumen por job",
                ["Job", "Corridas", "Duración promedio (s)", "Duración máxima (s)", "% fuera de SLA"],
                porJob),
            TabularWorkbookWriter.Sheet.OfText(
                "Corridas fuera de SLA",
                ["Job", "Resultado", "Duración (s)", "Inicio"],
                incumplidas),
        };

        return TabularWorkbookWriter.Write(sheets);
    }

    // ── "ict_webhooks": trazabilidad de entrega al gestor externo ───────────────────────────────

    public async Task<byte[]> BuildWebhooksAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var (fromUtc, toUtc) = BogotaDays.Range(from, to);
        var rows = new List<(Guid IdTransaction, string? Radicado, bool IsNotified, bool ResponseOk,
            int Attempts, string? TargetUrl, DateTimeOffset CreatedAt)>();

        await WithTenantAsync(tenantId, async (cmd, token) =>
        {
            cmd.CommandText = """
                SELECT id_transaction, manager_id_transaction, is_notified, response_ok,
                       attempts, target_url, created_at
                FROM ict.external_integration_webhook_master
                WHERE tenant_id = @tenant AND created_at >= @from AND created_at <= @to
                ORDER BY created_at DESC
                LIMIT @limite
                """;
            AddParam(cmd, "tenant", tenantId);
            AddParam(cmd, "from", fromUtc);
            AddParam(cmd, "to", toUtc);
            AddParam(cmd, "limite", MaxRows);

            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                rows.Add((
                    reader.GetGuid(reader.GetOrdinal("id_transaction")),
                    GetStringOrNull(reader, "manager_id_transaction"),
                    reader.GetBoolean(reader.GetOrdinal("is_notified")),
                    reader.GetBoolean(reader.GetOrdinal("response_ok")),
                    reader.GetInt16(reader.GetOrdinal("attempts")),
                    GetStringOrNull(reader, "target_url"),
                    reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at"))));
            }
        }, ct).ConfigureAwait(false);

        return BuildWebhooksExcel(rows);
    }

    private static byte[] BuildWebhooksExcel(
        List<(Guid IdTransaction, string? Radicado, bool IsNotified, bool ResponseOk,
            int Attempts, string? TargetUrl, DateTimeOffset CreatedAt)> rows)
    {
        var sheets = new List<TabularWorkbookWriter.Sheet>
        {
            TabularWorkbookWriter.Sheet.OfText(
                rows.Count >= MaxRows ? $"Webhooks (top {MaxRows})" : "Webhooks",
                ["Radicado", "Estado", "Intentos", "URL destino", "Fecha"],
                rows.Select(r => (IReadOnlyList<string>)
                [
                    r.Radicado ?? r.IdTransaction.ToString(),
                    EstadoWebhook(r.IsNotified, r.ResponseOk),
                    r.Attempts.ToString(Es),
                    r.TargetUrl ?? "",
                    r.CreatedAt.ToString("yyyy-MM-dd HH:mm", Es),
                ]).ToList()),
        };

        return TabularWorkbookWriter.Write(sheets);
    }

    internal static string EstadoWebhook(bool isNotified, bool responseOk) => (isNotified, responseOk) switch
    {
        (true, true) => "Entregado",
        (true, false) => "Fallido",
        _ => "Pendiente",
    };

    /// <summary>
    /// Clasifica un texto de comentarios de validación por coincidencia de subcadena contra la
    /// lista fija de causas conocidas (mayúsculas, sin acentos en las palabras clave). Null = no
    /// coincide con ninguna — se cuenta como "Otra/sin clasificar" en el resumen, nunca se descarta.
    /// </summary>
    internal static string? ClassifyCausa(string? comentarios)
    {
        var texto = (comentarios ?? string.Empty).ToUpperInvariant();
        foreach (var (causa, contiene) in CausasConocidas)
        {
            if (texto.Contains(contiene, StringComparison.Ordinal))
                return causa;
        }

        return null;
    }

    // ── Acceso a la base cross-schema ─────────────────────────────────────────────────────────

    /// <summary>Mismo patrón que <c>IctQueryRepository.WithTenantAsync</c>/<c>AlertMetricsReadRepository</c>:
    /// fija el GUC de tenant por RLS dentro de una transacción y ejecuta <paramref name="body"/>.</summary>
    private async Task WithTenantAsync(
        Guid tenantId, Func<DbCommand, CancellationToken, Task> body, CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)", cancellationToken)
                    .ConfigureAwait(false);

                var conn = context.Database.GetDbConnection();
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = transaction.GetDbTransaction();

                await body(cmd, cancellationToken).ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private static string? GetStringOrNull(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTimeOffset? GetDateTimeOrNull(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }

    internal static string Pct(int part, int total) =>
        total == 0 ? "0" : Math.Round(part * 100d / total, 2).ToString("0.##", Es);
}
