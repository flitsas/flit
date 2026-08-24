using System.Globalization;
using Flit.Admin.Domain.OtMetrics;
using Flit.Infrastructure.Documents.Reports;

namespace Flit.Infrastructure.Analytics.Scheduling;

/// <summary>
/// Reportes 2.0 (HU-D, tercera ola) — arma el adjunto de los 3 informes programados propios del
/// organismo de tránsito, uno por pestaña con rango de <c>OtReportsConsole.tsx</c> (eje invertido,
/// <see cref="IOtMetricsReadRepository"/>): "ot_analisis" (causales de rechazo + desempeño,
/// <see cref="BuildAnalisisAsync"/>), "ot_informe" (detalle trámite a trámite del periodo,
/// <see cref="BuildInformeAsync"/>) y "ot_revisores" (qué hizo cada persona,
/// <see cref="BuildRevisoresAsync"/>). "Ahora mismo" queda fuera a propósito: es un snapshot en
/// vivo sin rango, no tiene sentido como informe periódico. NO confundir con
/// <see cref="OtReportDocumentBuilder"/>, que arma el informe "ot" del lado de la EMPRESA gestora
/// auditando varios organismos — estos viven del lado del organismo.
/// </summary>
internal sealed class OtOwnReportDocumentBuilder(IOtMetricsReadRepository repo)
{
    private static readonly CultureInfo Es = CultureInfo.InvariantCulture;

    /// <summary>Mismo tope que el export manual de "Informe" en el navegador (OtReportBuilder.tsx).</summary>
    private const int InformeMaxRows = 2_000;
    private const int InformePageSize = 200;

    private static readonly Dictionary<string, string> EstadoLabel = new(StringComparer.Ordinal)
    {
        ["en_revision"] = "En revisión",
        ["esperando_placa"] = "Esperando placa",
        ["esperando_cliente"] = "Esperando al cliente",
        ["aprobado"] = "Aprobado",
        ["en_subsanacion"] = "En subsanación",
        ["rechazado"] = "Rechazado",
        ["anulado"] = "Anulado",
        ["otro"] = "Otro",
    };

    // Familias del catálogo (ADR-0050). Antes eran las dos modalidades —matricula_inicial y
    // traspaso—, que ya no coincidían con el dato que trae la fila: el reporte exportado imprimía el
    // código crudo porque el diccionario nunca acertaba.
    private static readonly Dictionary<string, string> FamiliaLabel = new(StringComparer.Ordinal)
    {
        ["MATRICULAS"] = "Matrículas",
        ["TRASPASO"] = "Traspaso",
        ["OTROS"] = "Otros",
    };

    // ── "ot_analisis": causales de rechazo + desempeño (OtAnalysisTab.tsx) ──────────────────────

    public async Task<byte[]?> BuildAnalisisAsync(
        Guid otTenantId, DateOnly from, DateOnly to, string format, CancellationToken ct)
    {
        var filter = new OtMetricsFilter(from, to);

        var performance = await repo.GetPerformanceAsync(otTenantId, filter, cancellationToken: ct)
            .ConfigureAwait(false);
        if (performance is null)
            return null; // Tenant sin organismo asociado: mismo criterio "sin adjunto" del resto del scheduler.

        var rejections = await repo.GetRejectionReasonsAsync(otTenantId, filter, cancellationToken: ct)
            .ConfigureAwait(false);

        return format == "pdf"
            ? BuildAnalisisPdf(from, to, performance, rejections)
            : BuildAnalisisExcel(performance, rejections);
    }

    private static byte[] BuildAnalisisExcel(OtPerformanceDto performance, OtRejectionReasonsDto? rejections)
    {
        var sheets = new List<TabularWorkbookWriter.Sheet>
        {
            TabularWorkbookWriter.Sheet.OfText(
                "Revisores",
                ["Revisor", "Decididos", "Aprobados", "Aprobación (%)", "Rechazados", "Rechazo (%)",
                    "Tiempo mediano (h)", "Vuelven a rechazarse (%)"],
                performance.Revisores.Select(r => (IReadOnlyList<string>)
                [
                    r.DisplayName, r.Decididos.ToString(Es), r.Aprobados.ToString(Es),
                    r.AprobacionPct.ToString("0.##", Es), r.Rechazados.ToString(Es),
                    r.RechazoPct.ToString("0.##", Es), Hours(r.TiempoMedianoHoras),
                    r.VuelvenARechazarsePct.ToString("0.##", Es),
                ]).ToList()),
            TabularWorkbookWriter.Sheet.OfText(
                "Calidad por empresa",
                ["Empresa", "Entregados", "Aprobados", "Pasan a la primera (%)", "Devoluciones promedio"],
                performance.Empresas.Select(e => (IReadOnlyList<string>)
                [
                    e.Name, e.Entregados.ToString(Es), e.Aprobados.ToString(Es),
                    e.PasanPrimeraPct.ToString("0.##", Es), e.DevolucionesPromedio.ToString("0.##", Es),
                ]).ToList()),
            TabularWorkbookWriter.Sheet.OfText(
                "Motivos de rechazo",
                ["Código", "Descripción", "Rechazos", "% de rechazos que la incluyen"],
                (rejections?.Causales ?? []).Select(c => (IReadOnlyList<string>)
                    [c.Code, c.Description, c.Rechazos.ToString(Es), c.Pct.ToString("0.##", Es)]).ToList()),
        };

        return TabularWorkbookWriter.Write(sheets);
    }

    private static byte[] BuildAnalisisPdf(
        DateOnly from, DateOnly to, OtPerformanceDto performance, OtRejectionReasonsDto? rejections)
    {
        var periodLabel = $"{from:yyyy-MM-dd} a {to:yyyy-MM-dd}";
        var sections = new List<TabularReportPdfGenerator.Section>
        {
            new(
                "Revisores",
                ["Revisor", "Decididos", "Aprobación (%)", "Rechazo (%)"],
                performance.Revisores.OrderByDescending(r => r.Decididos).Take(20)
                    .Select(r => (IReadOnlyList<string>)
                        [r.DisplayName, r.Decididos.ToString(Es), r.AprobacionPct.ToString("0.##", Es),
                            r.RechazoPct.ToString("0.##", Es)]).ToList()),
            new(
                "Motivos de rechazo más frecuentes",
                ["Código", "Descripción", "Rechazos", "% que la incluyen"],
                (rejections?.Causales ?? []).OrderByDescending(c => c.Rechazos).Take(15)
                    .Select(c => (IReadOnlyList<string>)
                        [c.Code, c.Description, c.Rechazos.ToString(Es), c.Pct.ToString("0.##", Es)]).ToList()),
        };

        return TabularReportPdfGenerator.Generate(
            "Informe programado FLIT", "Análisis del organismo", periodLabel, sections);
    }

    // ── "ot_informe": detalle trámite a trámite (OtReportBuilder.tsx) ───────────────────────────

    public async Task<byte[]?> BuildInformeAsync(
        Guid otTenantId, DateOnly from, DateOnly to, string format, CancellationToken ct)
    {
        var filter = new OtMetricsFilter(from, to);

        // Se recorre TODO el informe (hasta el tope), no solo la primera página: mismo criterio que
        // el export manual del navegador (OtReportBuilder.tsx, handleExport) — un correo automático
        // con 200 de 2000 filas sin avisarlo sería una trampa silenciosa.
        var rows = new List<OtReportRowDto>();
        OtReportSummaryDto? resumen = null;
        var total = 0;
        var page = 1;
        while (rows.Count < InformeMaxRows)
        {
            var query = new OtReportQuery(filter, page, InformePageSize, OtReportSort.Radicado, true);
            var result = await repo.GetReportAsync(otTenantId, query, cancellationToken: ct)
                .ConfigureAwait(false);
            if (result is null)
                return null; // Tenant sin organismo asociado.

            resumen ??= result.Resumen;
            total = result.Total;
            rows.AddRange(result.Filas);

            if (result.Filas.Count == 0 || rows.Count >= total)
                break;
            page++;
        }

        if (rows.Count > InformeMaxRows)
            rows = rows.Take(InformeMaxRows).ToList();

        return format == "pdf"
            ? BuildInformePdf(from, to, resumen!, rows, total)
            : BuildInformeExcel(resumen!, rows, total);
    }

    private static byte[] BuildInformeExcel(OtReportSummaryDto resumen, List<OtReportRowDto> rows, int total)
    {
        var sheets = new List<TabularWorkbookWriter.Sheet>
        {
            TabularWorkbookWriter.Sheet.OfText(
                "Resumen",
                ["Total", "En revisión", "Esperando placa", "Esperando cliente", "Aprobados",
                    "En subsanación", "Rechazados", "Anulados", "Devoluciones", "Tiempo mediano (h)"],
                (List<IReadOnlyList<string>>)
                    [[
                        resumen.Total.ToString(Es), resumen.EnRevision.ToString(Es),
                        resumen.EsperandoPlaca.ToString(Es), resumen.EsperandoCliente.ToString(Es),
                        resumen.Aprobados.ToString(Es), resumen.EnSubsanacion.ToString(Es),
                        resumen.Rechazados.ToString(Es), resumen.Anulados.ToString(Es),
                        resumen.Devoluciones.ToString(Es), Hours(resumen.TiempoMedianoHoras),
                    ]]),
            TabularWorkbookWriter.Sheet.OfText(
                total > rows.Count ? $"Detalle (top {rows.Count} de {total})" : "Detalle",
                ["Radicado", "Empresa", "Familia", "Placa", "VIN", "Estado", "Prioritario",
                    "Radicado el", "Última radicación", "Decidido el", "Tiempo decisión (h)",
                    "Días en el organismo", "Decidido por", "Devoluciones", "Causales último rechazo"],
                rows.Select(r => (IReadOnlyList<string>)
                [
                    r.ReferenceNumber, r.ClientTenantName, Label(FamiliaLabel, r.Familia),
                    r.Placa ?? "", r.Vin ?? "", Label(EstadoLabel, r.EstadoOt), SiNo(r.Prioritario),
                    r.RadicadoEn.ToString("yyyy-MM-dd", Es),
                    r.UltimaRadicacionEn?.ToString("yyyy-MM-dd", Es) ?? "",
                    r.DecididoEn?.ToString("yyyy-MM-dd", Es) ?? "",
                    Hours(r.HorasHastaDecision), r.DiasEnOrganismo?.ToString("0.#", Es) ?? "",
                    r.DecididoPor ?? "", r.Devoluciones.ToString(Es),
                    r.CausalesUltimoRechazo.Count == 0 ? "" : string.Join(" · ", r.CausalesUltimoRechazo),
                ]).ToList()),
        };

        return TabularWorkbookWriter.Write(sheets);
    }

    private static byte[] BuildInformePdf(
        DateOnly from, DateOnly to, OtReportSummaryDto resumen, List<OtReportRowDto> rows, int total)
    {
        var periodLabel = $"{from:yyyy-MM-dd} a {to:yyyy-MM-dd}";
        var sections = new List<TabularReportPdfGenerator.Section>
        {
            new(
                "Resumen",
                ["Total", "Aprobados", "Rechazados", "Anulados", "Tiempo mediano (h)"],
                (List<IReadOnlyList<string>>)
                    [[
                        resumen.Total.ToString(Es), resumen.Aprobados.ToString(Es),
                        resumen.Rechazados.ToString(Es), resumen.Anulados.ToString(Es),
                        Hours(resumen.TiempoMedianoHoras),
                    ]]),
            new(
                total > rows.Count ? $"Detalle (top {Math.Min(30, rows.Count)} de {total})" : "Detalle",
                ["Radicado", "Empresa", "Placa", "Estado", "Días en el organismo"],
                rows.Take(30).Select(r => (IReadOnlyList<string>)
                    [r.ReferenceNumber, r.ClientTenantName, r.Placa ?? "-", Label(EstadoLabel, r.EstadoOt),
                        r.DiasEnOrganismo?.ToString("0.#", Es) ?? "-"]).ToList()),
        };

        return TabularReportPdfGenerator.Generate(
            "Informe programado FLIT", "Informe del periodo", periodLabel, sections);
    }

    // ── "ot_revisores": qué hizo cada persona (OtReviewersTab.tsx) ──────────────────────────────

    public async Task<byte[]?> BuildRevisoresAsync(
        Guid otTenantId, DateOnly from, DateOnly to, string format, CancellationToken ct)
    {
        var filter = new OtMetricsFilter(from, to);
        var query = new OtReviewersQuery(filter, [], OtReviewerSort.Decididos, true);

        var result = await repo.GetReviewersReportAsync(otTenantId, query, cancellationToken: ct)
            .ConfigureAwait(false);
        if (result is null)
            return null;

        return format == "pdf"
            ? BuildRevisoresPdf(from, to, result)
            : BuildRevisoresExcel(result);
    }

    private static byte[] BuildRevisoresExcel(OtReviewersReportDto result)
    {
        var r = result.Resumen;
        var sheets = new List<TabularWorkbookWriter.Sheet>
        {
            TabularWorkbookWriter.Sheet.OfText(
                "Resumen",
                ["Revisores", "Decididos", "Aprobados", "Rechazados", "Aprobación (%)",
                    "Tiempo mediano (h)", "P90 (h)", "Concentración top (%)"],
                (List<IReadOnlyList<string>>)
                    [[
                        r.Revisores.ToString(Es), r.Decididos.ToString(Es), r.Aprobados.ToString(Es),
                        r.Rechazados.ToString(Es), r.AprobacionPct.ToString("0.##", Es),
                        Hours(r.TiempoMedianoHoras), Hours(r.TiempoP90Horas),
                        r.ConcentracionTopPct.ToString("0.##", Es),
                    ]]),
            TabularWorkbookWriter.Sheet.OfText(
                "Revisores",
                ["Revisor", "Decididos", "Aprobados", "Aprobación (%)", "Rechazados", "Rechazo (%)",
                    "Tiempo mediano (h)", "P90 (h)", "En menos de 24h (%)", "Vuelven a rechazarse (%)",
                    "Causales por rechazo", "Días activos", "Decisiones/día activo", "Empresas atendidas",
                    "Prioritarios decididos"],
                result.Filas.Select(f => (IReadOnlyList<string>)
                [
                    f.DisplayName, f.Decididos.ToString(Es), f.Aprobados.ToString(Es),
                    f.AprobacionPct.ToString("0.##", Es), f.Rechazados.ToString(Es),
                    f.RechazoPct.ToString("0.##", Es), Hours(f.TiempoMedianoHoras), Hours(f.TiempoP90Horas),
                    f.EnMenosDe24hPct.ToString("0.##", Es), f.VuelvenARechazarsePct.ToString("0.##", Es),
                    f.CausalesPorRechazo.ToString("0.##", Es), f.DiasActivos.ToString(Es),
                    f.DecisionesPorDiaActivo.ToString("0.##", Es), f.EmpresasAtendidas.ToString(Es),
                    f.PrioritariosDecididos.ToString(Es),
                ]).ToList()),
        };

        return TabularWorkbookWriter.Write(sheets);
    }

    private static byte[] BuildRevisoresPdf(DateOnly from, DateOnly to, OtReviewersReportDto result)
    {
        var periodLabel = $"{from:yyyy-MM-dd} a {to:yyyy-MM-dd}";
        var r = result.Resumen;
        var sections = new List<TabularReportPdfGenerator.Section>
        {
            new(
                "Resumen del equipo",
                ["Revisores", "Decididos", "Aprobación (%)", "Concentración top (%)"],
                (List<IReadOnlyList<string>>)
                    [[
                        r.Revisores.ToString(Es), r.Decididos.ToString(Es),
                        r.AprobacionPct.ToString("0.##", Es), r.ConcentracionTopPct.ToString("0.##", Es),
                    ]]),
            new(
                "Revisores",
                ["Revisor", "Decididos", "Aprobación (%)", "Tiempo mediano (h)"],
                result.Filas.OrderByDescending(f => f.Decididos).Take(30)
                    .Select(f => (IReadOnlyList<string>)
                        [f.DisplayName, f.Decididos.ToString(Es), f.AprobacionPct.ToString("0.##", Es),
                            Hours(f.TiempoMedianoHoras)]).ToList()),
        };

        return TabularReportPdfGenerator.Generate(
            "Informe programado FLIT", "Informe de revisores", periodLabel, sections);
    }

    // ── Utilidades ────────────────────────────────────────────────────────────────────────────

    private static string Label(Dictionary<string, string> map, string value) =>
        map.TryGetValue(value, out var label) ? label : value;

    private static string SiNo(bool value) => value ? "Sí" : "No";

    private static string Hours(double? h) => h is null ? "-" : h.Value.ToString("0.#", Es);
}
