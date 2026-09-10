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

// HU #11630 — en los 4 DTOs, "Total" es el UNIVERSO del periodo (un count(*) propio), no el largo
// de "Detalle": esa lista es solo la página pedida. "Page"/"PageSize" devuelven la página efectiva
// ya normalizada, misma convención que el motor de consultas (CompanyQueryResultDto). "Truncated"
// pasó a significar "el Excel no cabe entero" (Total > MaxRows), que es lo único que sigue cortando.

public sealed record IctNovedadesReportDto(
    IReadOnlyList<IctCausaResumenDto> ResumenPorCausa, IReadOnlyList<IctNovedadDetalleDto> Detalle, int Total, bool Truncated,
    int TotalPeriodoAnterior, int Page, int PageSize);

public sealed record IctAtascadoDto(string? Placa, string? Vin, string? Radicado, string Esperando, double DiasTranscurridos);

public sealed record IctAtascadosReportDto(
    IReadOnlyList<IctAtascadoDto> Detalle, int Total, bool Truncated, int Page, int PageSize);

public sealed record IctJobResumenDto(
    string Job, int Corridas, double DuracionPromedioSeg, double DuracionMaximaSeg, string PorcentajeFueraDeSlaTexto);

public sealed record IctJobIncumplidoDto(string Job, string Resultado, double DuracionSeg, DateTimeOffset Inicio);

/// <summary><paramref name="Total"/> son las corridas del periodo (universo de la variación);
/// <paramref name="TotalFueraDeSla"/> es el universo de la lista paginada
/// <paramref name="CorridasFueraDeSla"/>, que es un subconjunto del anterior.</summary>
public sealed record IctJobsReportDto(
    IReadOnlyList<IctJobResumenDto> ResumenPorJob, IReadOnlyList<IctJobIncumplidoDto> CorridasFueraDeSla, int Total, bool Truncated,
    int TotalPeriodoAnterior, int TotalFueraDeSla, int Page, int PageSize);

public sealed record IctWebhookDto(string Radicado, string Estado, int Intentos, string? UrlDestino, DateTimeOffset RegistradoEn);

/// <summary><paramref name="TotalEntregados"/>/<paramref name="TotalFallidos"/>/
/// <paramref name="TotalPendientes"/> son del PERIODO COMPLETO, no de la página: el estado se decide
/// fila a fila con <see cref="IctOwnReportDocumentBuilder.EstadoWebhook"/>, así que contarlo sobre
/// 50 filas de 5.210 daría un porcentaje inventado. Suman <paramref name="Total"/>.</summary>
public sealed record IctWebhooksReportDto(
    IReadOnlyList<IctWebhookDto> Detalle, int Total, bool Truncated, int TotalPeriodoAnterior, int Page, int PageSize,
    int TotalEntregados, int TotalFallidos, int TotalPendientes);

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

    /// <summary>Tope de filas de detalle del DOCUMENTO Excel (correo programado y exportación bajo
    /// demanda), que se sigue entregando de una sola pieza. La vista en vivo ya no usa este tope:
    /// pide una página (HU #11630).</summary>
    public const int MaxRows = 2_000;

    private static readonly (string Causa, string Contiene)[] CausasConocidas =
    [
        ("SOAT", "SOAT"),
        ("RTM", "RTM"),
        ("RNMC", "RNMC"),
        ("Documento faltante", "DOCUMENTO"),
    ];

    /// <summary>El mismo árbol de decisión de <see cref="ClassifyCausa"/>, pero en SQL, para poder
    /// agregar por causa sobre el periodo COMPLETO en vez de sobre las filas traídas. Se genera
    /// desde <see cref="CausasConocidas"/> justamente para que no puedan divergir; los literales son
    /// constantes de código, nunca entrada de usuario, así que no hay superficie de inyección.</summary>
    private static readonly string CausaCaseSql =
        "CASE" + string.Concat(CausasConocidas.Select(c => $" WHEN texto LIKE '%{c.Contiene}%' THEN '{c.Causa}'"))
        + " ELSE '' END";

    // ── "ict_novedades": novedades por causa (segmentado por subcadena en los comentarios) ─────

    public async Task<IctNovedadesReportDto> LoadNovedadesAsync(
        Guid tenantId, DateOnly from, DateOnly to, int page, int pageSize, CancellationToken ct)
    {
        var (fromUtc, toUtc) = BogotaDays.Range(from, to);
        var prev = PreviousRange(from, to);
        var (prevFromUtc, prevToUtc) = BogotaDays.Range(prev.From, prev.To);
        var (pagina, tamano, offset) = NormalizePage(page, pageSize);
        var rows = new List<(string? Placa, string? Vin, string? Radicado, string? Comentarios, DateTimeOffset CreatedAt)>();
        var porCausa = new List<(string Causa, int Cantidad)>();
        var previo = 0;

        await WithTenantAsync(tenantId, async (cmd, token) =>
        {
            // El resumen por causa —y con él el TOTAL del periodo— se agrega en SQL sobre el periodo
            // COMPLETO, no sobre las filas traídas: si saliera de la página, el resumen cambiaría al
            // pasar de página y el total sería el tamaño de la página (HU #11630, D1/D2).
            cmd.Parameters.Clear();
            cmd.CommandText = $"""
                SELECT {CausaCaseSql} AS causa, count(*) AS cantidad
                FROM (
                    SELECT upper(coalesce(business_comments_validation, '') || ' '
                                 || coalesce(external_comments_validation, '')) AS texto
                    FROM ict.external_integration_master
                    WHERE tenant_id = @tenant AND deleted_at IS NULL AND process_status_id = 4
                      AND created_at >= @from AND created_at <= @to
                ) clasificadas
                GROUP BY 1
                """;
            AddParam(cmd, "tenant", tenantId);
            AddParam(cmd, "from", fromUtc);
            AddParam(cmd, "to", toUtc);

            await using (var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                    porCausa.Add((reader.GetString(0), Convert.ToInt32(reader.GetValue(1), Es)));
            }

            // El detalle solo se pide si la página cae DENTRO del universo (HU #11630): sin esta
            // guarda, page=999999999 hacía que Postgres recorriera el índice entero para devolver
            // cero filas. Fuera de rango se devuelve una página vacía con el Total real — la misma
            // respuesta de antes, pero sin barrido y sin fingir que es la última página.
            if (offset < porCausa.Sum(c => c.Cantidad))
            {
                cmd.Parameters.Clear();
                cmd.CommandText = """
                    SELECT plate, vin, manager_id_transaction,
                           business_comments_validation, external_comments_validation, created_at
                    FROM ict.external_integration_master
                    WHERE tenant_id = @tenant AND deleted_at IS NULL AND process_status_id = 4
                      AND created_at >= @from AND created_at <= @to
                    ORDER BY created_at DESC, id DESC
                    LIMIT @limite OFFSET @offset
                    """;
                AddParam(cmd, "tenant", tenantId);
                AddParam(cmd, "from", fromUtc);
                AddParam(cmd, "to", toUtc);
                AddParam(cmd, "limite", tamano);
                AddParam(cmd, "offset", offset);

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

        return BuildNovedadesReport(rows, porCausa, previo, pagina, tamano);
    }

    internal static IctNovedadesReportDto BuildNovedadesReport(
        List<(string? Placa, string? Vin, string? Radicado, string? Comentarios, DateTimeOffset CreatedAt)> rows,
        IReadOnlyList<(string Causa, int Cantidad)> porCausa,
        int totalPeriodoAnterior, int page, int pageSize)
    {
        // Sin taxonomía de códigos: se clasifica por coincidencia de subcadena (mayúsculas, sin
        // acentos) contra una lista fija de causas conocidas; lo que no matchea cae en "Otra/sin
        // clasificar" — no se descarta, se cuenta igual para que el % del resumen sume el total.
        // La clasificación la hace SQL (ver CausaCaseSql): aquí solo se ordena y se saca el %.
        var conteos = porCausa.ToDictionary(c => c.Causa, c => c.Cantidad, StringComparer.Ordinal);
        var total = porCausa.Sum(c => c.Cantidad);
        var sinClasificar = conteos.GetValueOrDefault(string.Empty);

        var resumen = CausasConocidas
            .Select(c => conteos.GetValueOrDefault(c.Causa))
            .Select((cantidad, i) => new IctCausaResumenDto(CausasConocidas[i].Causa, cantidad, Pct(cantidad, total)))
            .Append(new IctCausaResumenDto("Otra/sin clasificar", sinClasificar, Pct(sinClasificar, total)))
            .ToList();

        var detalle = rows
            .Select(r => new IctNovedadDetalleDto(r.Placa, r.Vin, r.Radicado, r.Comentarios, r.CreatedAt))
            .ToList();

        return new IctNovedadesReportDto(
            resumen, detalle, total, Truncated: total > MaxRows, totalPeriodoAnterior, page, pageSize);
    }

    public async Task<byte[]> BuildNovedadesAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        // El documento no se pagina: sigue llevando el detalle completo hasta MaxRows, o sea la
        // primera (y única) página del tamaño del tope.
        var report = await LoadNovedadesAsync(tenantId, from, to, 1, MaxRows, ct).ConfigureAwait(false);
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

    public async Task<IctAtascadosReportDto> LoadAtascadosAsync(
        Guid tenantId, int page, int pageSize, CancellationToken ct)
    {
        var (pagina, tamano, offset) = NormalizePage(page, pageSize);
        var rows = new List<(string? Placa, string? Vin, string? Radicado, bool EsperandoNegocio, DateTimeOffset CreatedAt)>();
        var total = 0;

        await WithTenantAsync(tenantId, async (cmd, token) =>
        {
            // Total real del universo, independiente de la página (HU #11630, D2). Sin rango: este
            // informe es "ahora mismo", así que no pasa por CountAsync (que sí filtra por fechas).
            cmd.CommandText = """
                SELECT count(*)
                FROM ict.external_integration_master
                WHERE tenant_id = @tenant AND deleted_at IS NULL
                  AND process_status_id IN (1, 2) AND procedure_instance_id IS NULL
                """;
            AddParam(cmd, "tenant", tenantId);
            total = Convert.ToInt32(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false), Es);

            // El detalle solo se pide si la página cae DENTRO del universo (HU #11630): sin esta
            // guarda, page=999999999 hacía que Postgres recorriera el índice entero para devolver
            // cero filas. Fuera de rango se devuelve una página vacía con el Total real — la misma
            // respuesta de antes, pero sin barrido y sin fingir que es la última página.
            if (offset < total)
            {
                cmd.Parameters.Clear();
                cmd.CommandText = """
                    SELECT plate, vin, manager_id_transaction, business_date_validation, created_at
                    FROM ict.external_integration_master
                    WHERE tenant_id = @tenant AND deleted_at IS NULL
                      AND process_status_id IN (1, 2) AND procedure_instance_id IS NULL
                    ORDER BY created_at ASC, id ASC
                    LIMIT @limite OFFSET @offset
                    """;
                AddParam(cmd, "tenant", tenantId);
                AddParam(cmd, "limite", tamano);
                AddParam(cmd, "offset", offset);

                await using (var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
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
                }
            }
        }, ct).ConfigureAwait(false);

        return BuildAtascadosReport(rows, total, pagina, tamano);
    }

    internal static IctAtascadosReportDto BuildAtascadosReport(
        List<(string? Placa, string? Vin, string? Radicado, bool EsperandoNegocio, DateTimeOffset CreatedAt)> rows,
        int total, int page, int pageSize)
    {
        var hoy = DateTimeOffset.UtcNow;
        var detalle = rows
            .Select(r => new IctAtascadoDto(
                r.Placa, r.Vin, r.Radicado,
                r.EsperandoNegocio ? "Validación de negocio" : "Fuente externa (RUNT/RNMC/SOAT)",
                (hoy - r.CreatedAt).TotalDays))
            .ToList();

        return new IctAtascadosReportDto(detalle, total, Truncated: total > MaxRows, page, pageSize);
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
        var report = await LoadAtascadosAsync(tenantId, 1, MaxRows, ct).ConfigureAwait(false);
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
    public async Task<IctJobsReportDto> LoadJobsAsync(
        DateOnly from, DateOnly to, int page, int pageSize, CancellationToken ct)
    {
        var (fromUtc, toUtc) = BogotaDays.Range(from, to);
        var prev = PreviousRange(from, to);
        var (prevFromUtc, prevToUtc) = BogotaDays.Range(prev.From, prev.To);
        var (pagina, tamano, offset) = NormalizePage(page, pageSize);
        var porJob = new List<IctJobResumenDto>();
        var incumplidas = new List<IctJobIncumplidoDto>();
        var totalFueraDeSla = 0;
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

                // 1) Resumen por job agregado en SQL sobre el periodo COMPLETO. Antes se agregaba en
                // memoria sobre las (hasta MaxRows) filas traídas, así que con volumen el promedio y
                // el % fuera de SLA salían de una muestra, no del periodo (HU #11630, D2/D4).
                // avg() sobre integer devuelve numeric: se pide en float8 y se conserva en MILISEGUNDOS
                // hasta el último momento — redondear a 2 decimales DE SEGUNDO aplastaba a 0 todo
                // promedio por debajo de 5 ms (D3).
                cmd.CommandText = """
                    SELECT job_name,
                           count(*) AS corridas,
                           avg(duration_ms)::float8 AS promedio_ms,
                           max(duration_ms) AS maximo_ms,
                           count(*) FILTER (WHERE breached_sla) AS fuera_de_sla
                    FROM ict.job_runs
                    WHERE started_at >= @from AND started_at <= @to
                    GROUP BY job_name
                    ORDER BY job_name COLLATE "C"
                    """;
                AddParam(cmd, "from", fromUtc);
                AddParam(cmd, "to", toUtc);

                await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        var corridas = Convert.ToInt32(reader.GetValue(1), Es);
                        var fuera = Convert.ToInt32(reader.GetValue(4), Es);
                        totalFueraDeSla += fuera;
                        porJob.Add(new IctJobResumenDto(
                            reader.GetString(0),
                            corridas,
                            MsToSeg(reader.GetDouble(2)),
                            MsToSeg(reader.GetInt32(3)),
                            Pct(fuera, corridas)));
                    }
                }

                // 2) Página de corridas fuera de SLA — es la lista de detalle de este informe. Solo se
                // pide si la página cae DENTRO de su universo (que es totalFueraDeSla, no las corridas
                // totales): sin esta guarda, page=999999999 recorría el índice entero para nada.
                if (offset < totalFueraDeSla)
                {
                    cmd.Parameters.Clear();
                    cmd.CommandText = """
                        SELECT job_name, outcome, duration_ms, started_at
                        FROM ict.job_runs
                        WHERE started_at >= @from AND started_at <= @to AND breached_sla
                        ORDER BY started_at DESC, id DESC
                        LIMIT @limite OFFSET @offset
                        """;
                    AddParam(cmd, "from", fromUtc);
                    AddParam(cmd, "to", toUtc);
                    AddParam(cmd, "limite", tamano);
                    AddParam(cmd, "offset", offset);

                    await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        incumplidas.Add(new IctJobIncumplidoDto(
                            reader.GetString(0),
                            reader.GetString(1),
                            MsToSeg(reader.GetInt32(2)),
                            reader.GetFieldValue<DateTimeOffset>(3)));
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

        return BuildJobsReport(porJob, incumplidas, totalFueraDeSla, previo, pagina, tamano);
    }

    /// <summary>
    /// Arma el DTO de jobs a partir de lo que ya trajo SQL. Extraído de <see cref="LoadJobsAsync"/>
    /// (HU #11630) para que quede cubierto por pruebas, igual que los otros tres informes: era el
    /// único que construía su DTO inline y por eso ni <c>Truncated</c> ni <c>Total</c> tenían forma
    /// de probarse sin Postgres.
    ///
    /// <para><paramref name="totalFueraDeSla"/> llega como parámetro y no se deriva de
    /// <paramref name="porJob"/> porque <see cref="IctJobResumenDto"/> guarda el incumplimiento como
    /// PORCENTAJE ya formateado, no como conteo: reconstruir el entero desde ese texto sería
    /// redondear dos veces. <c>Total</c> sí se deriva, que ahí sí está el dato crudo.</para>
    /// </summary>
    internal static IctJobsReportDto BuildJobsReport(
        IReadOnlyList<IctJobResumenDto> porJob,
        IReadOnlyList<IctJobIncumplidoDto> corridasFueraDeSla,
        int totalFueraDeSla,
        int totalPeriodoAnterior,
        int page,
        int pageSize)
    {
        // Corridas del periodo = las que ya contó el GROUP BY por job, sumadas.
        var total = porJob.Sum(j => j.Corridas);

        // Truncated mide lo único que el Excel puede cortar, y en este informe esa lista es
        // CorridasFueraDeSla — no las corridas totales (HU #11630): con 2.500 corridas y 10 fuera de
        // SLA el documento sale entero. Es el mismo criterio que ya usa BuildJobsExcel.
        return new IctJobsReportDto(
            porJob, corridasFueraDeSla, total, Truncated: totalFueraDeSla > MaxRows, totalPeriodoAnterior,
            totalFueraDeSla, page, pageSize);
    }

    public async Task<byte[]> BuildJobsAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        _ = tenantId;
        var report = await LoadJobsAsync(from, to, 1, MaxRows, ct).ConfigureAwait(false);
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
                    r.Job, r.Corridas.ToString(Es), r.DuracionPromedioSeg.ToString(SegFormat, Es),
                    r.DuracionMaximaSeg.ToString(SegFormat, Es), r.PorcentajeFueraDeSlaTexto,
                ]).ToList()),
            TabularWorkbookWriter.Sheet.OfText(
                report.TotalFueraDeSla > MaxRows ? $"Corridas fuera de SLA (top {MaxRows})" : "Corridas fuera de SLA",
                ["Job", "Resultado", "Duración (s)", "Inicio"],
                report.CorridasFueraDeSla.Select(r => (IReadOnlyList<string>)
                [
                    r.Job, r.Resultado, r.DuracionSeg.ToString(SegFormat, Es),
                    r.Inicio.ToString("yyyy-MM-dd HH:mm", Es),
                ]).ToList()),
        };

        return TabularWorkbookWriter.Write(sheets);
    }

    // ── "ict_webhooks": trazabilidad de entrega al gestor externo ───────────────────────────────

    public async Task<IctWebhooksReportDto> LoadWebhooksAsync(
        Guid tenantId, DateOnly from, DateOnly to, int page, int pageSize, CancellationToken ct)
    {
        var (fromUtc, toUtc) = BogotaDays.Range(from, to);
        var prev = PreviousRange(from, to);
        var (prevFromUtc, prevToUtc) = BogotaDays.Range(prev.From, prev.To);
        var (pagina, tamano, offset) = NormalizePage(page, pageSize);
        var rows = new List<(Guid IdTransaction, string? Radicado, bool IsNotified, bool ResponseOk,
            int Attempts, string? TargetUrl, DateTimeOffset CreatedAt)>();
        var total = 0;
        var entregados = 0;
        var fallidos = 0;
        var pendientes = 0;
        var previo = 0;

        await WithTenantAsync(tenantId, async (cmd, token) =>
        {
            // Total real del periodo, independiente de la página (HU #11630, D1/D2): antes el KPI
            // mostraba el largo de la lista topada y la variación comparaba ese tope contra un
            // count(*) sin tope, inventando caídas. Los 3 contadores por estado cuelgan de ESTE
            // mismo count(*) con FILTER: no hay viaje extra a la tabla.
            cmd.CommandText = $"""
                SELECT count(*) AS total,
                       count(*) FILTER (WHERE {EstadoWebhookSql(EstadoEntregado)}) AS entregados,
                       count(*) FILTER (WHERE {EstadoWebhookSql(EstadoFallido)}) AS fallidos,
                       count(*) FILTER (WHERE {EstadoWebhookSql(EstadoPendiente)}) AS pendientes
                FROM ict.external_integration_webhook_master
                WHERE tenant_id = @tenant AND created_at >= @from AND created_at <= @to
                """;
            AddParam(cmd, "tenant", tenantId);
            AddParam(cmd, "from", fromUtc);
            AddParam(cmd, "to", toUtc);

            await using (var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                if (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    total = Convert.ToInt32(reader.GetValue(0), Es);
                    entregados = Convert.ToInt32(reader.GetValue(1), Es);
                    fallidos = Convert.ToInt32(reader.GetValue(2), Es);
                    pendientes = Convert.ToInt32(reader.GetValue(3), Es);
                }
            }

            // El detalle solo se pide si la página cae DENTRO del universo (HU #11630): sin esta
            // guarda, page=999999999 hacía que Postgres recorriera el índice entero para devolver
            // cero filas. Fuera de rango se devuelve una página vacía con el Total real — la misma
            // respuesta de antes, pero sin barrido y sin fingir que es la última página.
            if (offset < total)
            {
                cmd.Parameters.Clear();
                cmd.CommandText = """
                    SELECT id_transaction, manager_id_transaction, is_notified, response_ok,
                           attempts, target_url, created_at
                    FROM ict.external_integration_webhook_master
                    WHERE tenant_id = @tenant AND created_at >= @from AND created_at <= @to
                    ORDER BY created_at DESC, id DESC
                    LIMIT @limite OFFSET @offset
                    """;
                AddParam(cmd, "tenant", tenantId);
                AddParam(cmd, "from", fromUtc);
                AddParam(cmd, "to", toUtc);
                AddParam(cmd, "limite", tamano);
                AddParam(cmd, "offset", offset);

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
            }

            previo = await CountAsync(cmd, """
                SELECT count(*)
                FROM ict.external_integration_webhook_master
                WHERE tenant_id = @tenant AND created_at >= @from AND created_at <= @to
                """, tenantId, prevFromUtc, prevToUtc, token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);

        return BuildWebhooksReport(rows, total, previo, pagina, tamano, entregados, fallidos, pendientes);
    }

    internal static IctWebhooksReportDto BuildWebhooksReport(
        List<(Guid IdTransaction, string? Radicado, bool IsNotified, bool ResponseOk,
            int Attempts, string? TargetUrl, DateTimeOffset CreatedAt)> rows,
        int total, int totalPeriodoAnterior, int page, int pageSize,
        int totalEntregados, int totalFallidos, int totalPendientes)
    {
        var detalle = rows
            .Select(r => new IctWebhookDto(
                r.Radicado ?? r.IdTransaction.ToString(),
                EstadoWebhook(r.IsNotified, r.ResponseOk),
                r.Attempts,
                r.TargetUrl,
                r.CreatedAt))
            .ToList();

        return new IctWebhooksReportDto(
            detalle, total, Truncated: total > MaxRows, totalPeriodoAnterior, page, pageSize,
            totalEntregados, totalFallidos, totalPendientes);
    }

    public async Task<byte[]> BuildWebhooksAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var report = await LoadWebhooksAsync(tenantId, from, to, 1, MaxRows, ct).ConfigureAwait(false);
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

    internal const string EstadoEntregado = "Entregado";
    internal const string EstadoFallido = "Fallido";
    internal const string EstadoPendiente = "Pendiente";

    internal static string EstadoWebhook(bool isNotified, bool responseOk) => (isNotified, responseOk) switch
    {
        (true, true) => EstadoEntregado,
        (true, false) => EstadoFallido,
        _ => EstadoPendiente,
    };

    /// <summary>Las 4 combinaciones posibles de (<c>is_notified</c>, <c>response_ok</c>).</summary>
    private static readonly (bool IsNotified, bool ResponseOk)[] CombinacionesWebhook =
        [(false, false), (false, true), (true, false), (true, true)];

    /// <summary>
    /// Predicado SQL de un estado de webhook, DERIVADO de <see cref="EstadoWebhook"/>: se enumeran
    /// las 4 combinaciones de (<c>is_notified</c>, <c>response_ok</c>) —las dos únicas columnas de
    /// las que depende la función, y las dos están en la tabla que se cuenta— y se conservan las que
    /// la propia función clasifica en ese estado. Mismo patrón que <see cref="CausaCaseSql"/>: la
    /// regla sigue viviendo en C# y el SQL se genera de ella, así que no pueden divergir.
    /// </summary>
    private static string EstadoWebhookSql(string estado)
    {
        var terminos = CombinacionesWebhook
            .Where(c => string.Equals(EstadoWebhook(c.IsNotified, c.ResponseOk), estado, StringComparison.Ordinal))
            .Select(c => $"(is_notified = {Literal(c.IsNotified)} AND response_ok = {Literal(c.ResponseOk)})")
            .ToList();

        return terminos.Count == 0 ? "false" : string.Join(" OR ", terminos);

        static string Literal(bool valor) => valor ? "true" : "false";
    }

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

    /// <summary>
    /// Normaliza la página con la misma regla que el motor de consultas
    /// (<c>QueryNormalizer.BuildRequest</c>): 1-based, tamaño acotado. El tope de aquí es
    /// <see cref="MaxRows"/> y no <c>QueryLimits.MaxPageSize</c> porque el camino de exportación pide
    /// una única página del tamaño del documento completo; el acotado a 200 para pantalla lo aplica
    /// el endpoint, igual que el resto de analítica (clamp en el borde + clamp en el repositorio).
    /// </summary>
    internal static (int Page, int PageSize, long Offset) NormalizePage(int page, int pageSize)
    {
        var pagina = Math.Max(1, page);
        var tamano = Math.Clamp(pageSize, 1, MaxRows);

        // El offset se calcula en long A PROPÓSITO (HU #11630): con page=10.737.420 y pageSize=200
        // el producto desborda int y da −2.147.483.496, que Postgres rechaza con "OFFSET must not be
        // negative" — un 500 en los 4 endpoints de lectura con solo poner un número grande en la
        // query. En long no desborda (máximo ~4,3e12) y OFFSET acepta bigint, así que una página más
        // allá del final devuelve simplemente una página vacía con el Total real, que es lo mismo
        // que hace el motor de consultas (Skip() por encima del universo, CompanyQueryRepository).
        return (pagina, tamano, (long)(pagina - 1) * tamano);
    }

    /// <summary>
    /// Milisegundos (como los guarda <c>ict.job_runs.duration_ms</c>, entero) a segundos, con
    /// resolución de décima de milisegundo. Redondear a 2 decimales de SEGUNDO —lo que se hacía
    /// antes— aplastaba a 0 todo lo que durara menos de 5 ms, que es la mayoría de las corridas
    /// (HU #11630, D3): la precisión se perdía en la conversión, no en el dato de origen.
    /// </summary>
    internal static double MsToSeg(double durationMs) => Math.Round(durationMs / 1000d, 4);

    /// <summary>Formato de las duraciones en el Excel, alineado con la resolución de <see cref="MsToSeg"/>.</summary>
    private const string SegFormat = "0.####";

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
