using System.Data.Common;
using System.Globalization;
using Flit.Infrastructure.Documents.Reports;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Flit.Infrastructure.Analytics.Scheduling;

// ── Formas de reporte (HU #11617) ───────────────────────────────────────────────────────────
//
// Cada "Load*Async" de abajo calcula EXACTAMENTE el mismo resumen + detalle que antes solo vivía
// dentro del Excel — ahora también lo consume IctReportsEndpoints (vista en vivo) sin repetir la
// consulta SQL ni la agregación. Los "Build*Async" (usados por el scheduler de correo) llaman al
// Load correspondiente y solo agregan la escritura a Excel.

public sealed record IctCausaResumenDto(string Causa, int Cantidad, string PorcentajeTexto);

public sealed record IctNovedadDetalleDto(string? Placa, string? Vin, string? Radicado, string? Comentarios, DateTimeOffset RegistradoEn);

public sealed record IctNovedadesReportDto(
    IReadOnlyList<IctCausaResumenDto> ResumenPorCausa, IReadOnlyList<IctNovedadDetalleDto> Detalle, int Total, bool Truncated,
    int TotalPeriodoAnterior);

public sealed record IctAtascadoDto(string? Placa, string? Vin, string? Radicado, string Esperando, double DiasTranscurridos);

public sealed record IctAtascadosReportDto(IReadOnlyList<IctAtascadoDto> Detalle, int Total, bool Truncated);

public sealed record IctJobResumenDto(
    string Job, int Corridas, double DuracionPromedioSeg, double DuracionMaximaSeg, string PorcentajeFueraDeSlaTexto);

public sealed record IctJobIncumplidoDto(string Job, string Resultado, double DuracionSeg, DateTimeOffset Inicio);

public sealed record IctJobsReportDto(
    IReadOnlyList<IctJobResumenDto> ResumenPorJob, IReadOnlyList<IctJobIncumplidoDto> CorridasFueraDeSla, int Total, bool Truncated,
    int TotalPeriodoAnterior);

public sealed record IctWebhookDto(string Radicado, string Estado, int Intentos, string? UrlDestino, DateTimeOffset RegistradoEn);

public sealed record IctWebhooksReportDto(
    IReadOnlyList<IctWebhookDto> Detalle, int Total, bool Truncated, int TotalPeriodoAnterior);

/// <summary>
/// Reportes 2.0 (HU-D) — calcula y arma el adjunto Excel de los 4 informes de alcance ICT
/// (Integración con Terceros): "ict_novedades" (<see cref="BuildNovedadesAsync"/>), "ict_atascados"
/// (<see cref="BuildAtascadosAsync"/>), "ict_jobs" (<see cref="BuildJobsAsync"/>, platform-wide,
/// SuperAdmin-only — la restricción vive en <c>ReportSchedulesEndpoints</c>/<c>IctReportsEndpoints</c>,
/// no aquí) y "ict_webhooks" (<see cref="BuildWebhooksAsync"/>). Los 4 son SOLO Excel (§
/// <c>SchedulingValidation</c>), igual que "consulta" — no hay una versión PDF con sentido para un
/// detalle fila a fila.
///
/// <para><b>Cross-schema, no cross-servicio</b>, mismo patrón que <see cref="IctQueryRepository"/>
/// y <see cref="AlertMetricsReadRepository"/>: SQL crudo sobre la conexión de
/// <see cref="FlitDbContext"/> contra <c>ict.*</c> (propiedad de core-ict, mismo Postgres), fijando
/// el GUC <c>app.current_tenant_id</c> por RLS y filtrando además <c>tenant_id</c> explícito. No se
/// reutiliza el <c>WithTenantAsync</c> privado de <see cref="IctQueryRepository"/> — vive en una
/// clase distinta con su propio ciclo de vida de DI; se replica aquí el mismo patrón corto en vez de
/// exponerlo como compartido, que hubiera acoplado dos módulos por una utilidad de 15 líneas.</para>
///
/// <para><b>Una sola agregación, dos consumidores</b> (HU #11617): los métodos "Load*Async" calculan
/// el resumen + detalle y no saben nada de Excel; <c>IctReportsEndpoints</c> los expone en vivo tal
/// cual, y los "Build*Async" de abajo los convierten a <c>.xlsx</c> para el correo programado. Antes
/// (HU #11609) solo existían los "Build*Async"; separar el cálculo evita que la vista en vivo y el
/// adjunto de correo puedan divergir con el tiempo.</para>
/// </summary>
public sealed class IctOwnReportDocumentBuilder(FlitDbContext context)
{
    private static readonly CultureInfo Es = CultureInfo.InvariantCulture;

    /// <summary>Tope de filas de detalle por informe — mismo criterio tanto para el Excel del
    /// correo programado como para la vista en vivo (informe automático/consulta puntual, no
    /// exploración interactiva paginada).</summary>
    public const int MaxRows = 2_000;

    private static readonly (string Causa, string Contiene)[] CausasConocidas =
    [
        ("SOAT", "SOAT"),
        ("RTM", "RTM"),
        ("RNMC", "RNMC"),
        ("Documento faltante", "DOCUMENTO"),
    ];

    // ── "ict_novedades": novedades por causa (segmentado por subcadena en los comentarios) ─────

    public async Task<IctNovedadesReportDto> LoadNovedadesAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var (fromUtc, toUtc) = BogotaDays.Range(from, to);
        var prev = PreviousRange(from, to);
        var (prevFromUtc, prevToUtc) = BogotaDays.Range(prev.From, prev.To);
        var rows = new List<(string? Placa, string? Vin, string? Radicado, string? Comentarios, DateTimeOffset CreatedAt)>();
        var previo = 0;

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

            await using (var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
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
            }

            // Solo el CONTEO del periodo anterior, no sus filas: la variación necesita un número, y
            // traerse el detalle de un periodo que nadie va a mirar dobla el costo de cada carga.
            previo = await CountAsync(cmd, """
                SELECT count(*)
                FROM ict.external_integration_master
                WHERE tenant_id = @tenant AND deleted_at IS NULL AND process_status_id = 4
                  AND created_at >= @from AND created_at <= @to
                """, tenantId, prevFromUtc, prevToUtc, token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);

        return BuildNovedadesReport(rows, previo);
    }

    private static IctNovedadesReportDto BuildNovedadesReport(
        List<(string? Placa, string? Vin, string? Radicado, string? Comentarios, DateTimeOffset CreatedAt)> rows,
        int totalPeriodoAnterior)
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
        var resumen = porCausa
            .Select(kv => new IctCausaResumenDto(kv.Key, kv.Value, Pct(kv.Value, total)))
            .Append(new IctCausaResumenDto("Otra/sin clasificar", sinClasificar, Pct(sinClasificar, total)))
            .ToList();

        var detalle = rows
            .Select(r => new IctNovedadDetalleDto(r.Placa, r.Vin, r.Radicado, r.Comentarios, r.CreatedAt))
            .ToList();

        return new IctNovedadesReportDto(resumen, detalle, total, Truncated: total >= MaxRows, totalPeriodoAnterior);
    }

    public async Task<byte[]> BuildNovedadesAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var report = await LoadNovedadesAsync(tenantId, from, to, ct).ConfigureAwait(false);
        return BuildNovedadesExcel(report);
    }

    private static byte[] BuildNovedadesExcel(IctNovedadesReportDto report)
    {
        var sheets = new List<TabularWorkbookWriter.Sheet>
        {
            TabularWorkbookWriter.Sheet.OfText(
                "Resumen por causa", ["Causa", "Cantidad", "% del total"],
                report.ResumenPorCausa.Select(r => (IReadOnlyList<string>)
                    [r.Causa, r.Cantidad.ToString(Es), r.PorcentajeTexto]).ToList()),
            TabularWorkbookWriter.Sheet.OfText(
                report.Truncated ? $"Detalle (top {MaxRows})" : "Detalle",
                ["Placa", "VIN", "Radicado", "Comentarios", "Fecha de registro"],
                report.Detalle.Select(r => (IReadOnlyList<string>)
                [
                    r.Placa ?? "", r.Vin ?? "", r.Radicado ?? "", r.Comentarios ?? "",
                    r.RegistradoEn.ToString("yyyy-MM-dd HH:mm", Es),
                ]).ToList()),
        };

        return TabularWorkbookWriter.Write(sheets);
    }

    // ── "ict_atascados": pre-trámites que siguen sin borrador HOY (sin filtro de rango) ────────

    public async Task<IctAtascadosReportDto> LoadAtascadosAsync(Guid tenantId, CancellationToken ct)
    {
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

        return BuildAtascadosReport(rows);
    }

    private static IctAtascadosReportDto BuildAtascadosReport(
        List<(string? Placa, string? Vin, string? Radicado, bool EsperandoNegocio, DateTimeOffset CreatedAt)> rows)
    {
        var hoy = DateTimeOffset.UtcNow;
        var detalle = rows
            .Select(r => new IctAtascadoDto(
                r.Placa, r.Vin, r.Radicado,
                r.EsperandoNegocio ? "Validación de negocio" : "Fuente externa (RUNT/RNMC/SOAT)",
                (hoy - r.CreatedAt).TotalDays))
            .ToList();

        return new IctAtascadosReportDto(detalle, rows.Count, Truncated: rows.Count >= MaxRows);
    }

    /// <summary>
    /// Firma con <paramref name="from"/>/<paramref name="to"/> por uniformidad con los otros 3
    /// tipos (mismo <c>BuildAttachmentAsync</c> del scheduler los invoca a todos igual) — se
    /// ignoran a propósito, ver <see cref="LoadAtascadosAsync"/>.
    /// </summary>
    public async Task<byte[]> BuildAtascadosAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        _ = from;
        _ = to;
        var report = await LoadAtascadosAsync(tenantId, ct).ConfigureAwait(false);
        return BuildAtascadosExcel(report);
    }

    private static byte[] BuildAtascadosExcel(IctAtascadosReportDto report)
    {
        var sheets = new List<TabularWorkbookWriter.Sheet>
        {
            TabularWorkbookWriter.Sheet.OfText(
                report.Truncated ? $"Atascados (top {MaxRows})" : "Atascados",
                ["Placa", "VIN", "Radicado", "Esperando", "Días desde el registro"],
                report.Detalle.Select(r => (IReadOnlyList<string>)
                [
                    r.Placa ?? "", r.Vin ?? "", r.Radicado ?? "", r.Esperando,
                    r.DiasTranscurridos.ToString("0.#", Es),
                ]).ToList()),
        };

        return TabularWorkbookWriter.Write(sheets);
    }

    // ── "ict_jobs": rendimiento de los jobs del pipeline (platform-wide, SuperAdmin-only) ──────

    /// <summary>
    /// <c>ict.job_runs</c> es GLOBAL de plataforma (sin <c>tenant_id</c>, sin RLS — ver el DDL
    /// <c>16-ICT-job-runs.sql</c> de core-ict). La restricción de que solo SuperAdmin pueda
    /// consultarlo (programado o en vivo) vive en la capa de autorización
    /// (<c>ReportSchedulesEndpoints</c>/<c>IctReportsEndpoints</c>), no aquí.
    /// </summary>
    public async Task<IctJobsReportDto> LoadJobsAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var (fromUtc, toUtc) = BogotaDays.Range(from, to);
        var prev = PreviousRange(from, to);
        var (prevFromUtc, prevToUtc) = BogotaDays.Range(prev.From, prev.To);
        var rows = new List<(string JobName, string Outcome, bool BreachedSla, int DurationMs, DateTimeOffset StartedAt)>();
        var previo = 0;

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

                await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
                {
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

                cmd.Parameters.Clear();
                cmd.CommandText = """
                    SELECT count(*) FROM ict.job_runs
                    WHERE started_at >= @from AND started_at <= @to
                    """;
                AddParam(cmd, "from", prevFromUtc);
                AddParam(cmd, "to", prevToUtc);
                previo = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), Es);
            }
            finally
            {
                if (wasClosed)
                    await conn.CloseAsync().ConfigureAwait(false);
            }
        }).ConfigureAwait(false);

        return BuildJobsReport(rows, previo);
    }

    private static IctJobsReportDto BuildJobsReport(
        List<(string JobName, string Outcome, bool BreachedSla, int DurationMs, DateTimeOffset StartedAt)> rows,
        int totalPeriodoAnterior)
    {
        var porJob = rows
            .GroupBy(r => r.JobName, StringComparer.Ordinal)
            .Select(g => new IctJobResumenDto(
                g.Key,
                g.Count(),
                Math.Round(g.Average(r => r.DurationMs) / 1000d, 2),
                Math.Round(g.Max(r => r.DurationMs) / 1000d, 2),
                Pct(g.Count(r => r.BreachedSla), g.Count())))
            .OrderBy(r => r.Job, StringComparer.Ordinal)
            .ToList();

        var incumplidas = rows.Where(r => r.BreachedSla)
            .OrderByDescending(r => r.StartedAt)
            .Take(MaxRows)
            .Select(r => new IctJobIncumplidoDto(r.JobName, r.Outcome, Math.Round(r.DurationMs / 1000d, 2), r.StartedAt))
            .ToList();

        return new IctJobsReportDto(porJob, incumplidas, rows.Count, Truncated: rows.Count >= MaxRows, totalPeriodoAnterior);
    }

    public async Task<byte[]> BuildJobsAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        _ = tenantId;
        var report = await LoadJobsAsync(from, to, ct).ConfigureAwait(false);
        return BuildJobsExcel(report);
    }

    private static byte[] BuildJobsExcel(IctJobsReportDto report)
    {
        var sheets = new List<TabularWorkbookWriter.Sheet>
        {
            TabularWorkbookWriter.Sheet.OfText(
                "Resumen por job",
                ["Job", "Corridas", "Duración promedio (s)", "Duración máxima (s)", "% fuera de SLA"],
                report.ResumenPorJob.Select(r => (IReadOnlyList<string>)
                [
                    r.Job, r.Corridas.ToString(Es), r.DuracionPromedioSeg.ToString("0.##", Es),
                    r.DuracionMaximaSeg.ToString("0.##", Es), r.PorcentajeFueraDeSlaTexto,
                ]).ToList()),
            TabularWorkbookWriter.Sheet.OfText(
                "Corridas fuera de SLA",
                ["Job", "Resultado", "Duración (s)", "Inicio"],
                report.CorridasFueraDeSla.Select(r => (IReadOnlyList<string>)
                [
                    r.Job, r.Resultado, r.DuracionSeg.ToString("0.##", Es),
                    r.Inicio.ToString("yyyy-MM-dd HH:mm", Es),
                ]).ToList()),
        };

        return TabularWorkbookWriter.Write(sheets);
    }

    // ── "ict_webhooks": trazabilidad de entrega al gestor externo ───────────────────────────────

    public async Task<IctWebhooksReportDto> LoadWebhooksAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var (fromUtc, toUtc) = BogotaDays.Range(from, to);
        var prev = PreviousRange(from, to);
        var (prevFromUtc, prevToUtc) = BogotaDays.Range(prev.From, prev.To);
        var rows = new List<(Guid IdTransaction, string? Radicado, bool IsNotified, bool ResponseOk,
            int Attempts, string? TargetUrl, DateTimeOffset CreatedAt)>();
        var previo = 0;

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

            await using (var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
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
            }

            previo = await CountAsync(cmd, """
                SELECT count(*)
                FROM ict.external_integration_webhook_master
                WHERE tenant_id = @tenant AND created_at >= @from AND created_at <= @to
                """, tenantId, prevFromUtc, prevToUtc, token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);

        return BuildWebhooksReport(rows, previo);
    }

    private static IctWebhooksReportDto BuildWebhooksReport(
        List<(Guid IdTransaction, string? Radicado, bool IsNotified, bool ResponseOk,
            int Attempts, string? TargetUrl, DateTimeOffset CreatedAt)> rows,
        int totalPeriodoAnterior)
    {
        var detalle = rows
            .Select(r => new IctWebhookDto(
                r.Radicado ?? r.IdTransaction.ToString(),
                EstadoWebhook(r.IsNotified, r.ResponseOk),
                r.Attempts,
                r.TargetUrl,
                r.CreatedAt))
            .ToList();

        return new IctWebhooksReportDto(detalle, rows.Count, Truncated: rows.Count >= MaxRows, totalPeriodoAnterior);
    }

    public async Task<byte[]> BuildWebhooksAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var report = await LoadWebhooksAsync(tenantId, from, to, ct).ConfigureAwait(false);
        return BuildWebhooksExcel(report);
    }

    private static byte[] BuildWebhooksExcel(IctWebhooksReportDto report)
    {
        var sheets = new List<TabularWorkbookWriter.Sheet>
        {
            TabularWorkbookWriter.Sheet.OfText(
                report.Truncated ? $"Webhooks (top {MaxRows})" : "Webhooks",
                ["Radicado", "Estado", "Intentos", "URL destino", "Fecha"],
                report.Detalle.Select(r => (IReadOnlyList<string>)
                [
                    r.Radicado, r.Estado, r.Intentos.ToString(Es), r.UrlDestino ?? "",
                    r.RegistradoEn.ToString("yyyy-MM-dd HH:mm", Es),
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

    /// <summary>
    /// Ventana inmediatamente anterior, de la MISMA longitud en días, para la variación de las
    /// tarjetas ("vs comparado"). Se compara contra el periodo previo y no contra el año anterior
    /// porque ICT lleva meses, no años, de historia: un "vs año anterior" saldría siempre sin base.
    /// Ambos extremos son inclusivos, igual que el rango que elige el usuario.
    /// </summary>
    internal static (DateOnly From, DateOnly To) PreviousRange(DateOnly from, DateOnly to)
    {
        var dias = to.DayNumber - from.DayNumber + 1;
        var prevTo = from.AddDays(-1);
        return (prevTo.AddDays(-(dias - 1)), prevTo);
    }

    /// <summary>Cuenta filas del periodo anterior reutilizando el comando (y la transacción con el
    /// GUC de tenant ya fijado) de la consulta principal.</summary>
    private static async Task<int> CountAsync(
        DbCommand cmd, string sql, Guid tenantId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct)
    {
        cmd.Parameters.Clear();
        cmd.CommandText = sql;
        AddParam(cmd, "tenant", tenantId);
        AddParam(cmd, "from", fromUtc);
        AddParam(cmd, "to", toUtc);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), Es);
    }
}
