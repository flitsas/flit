using System.Globalization;
using Flit.Admin.Domain.OtMetrics;
using Flit.Infrastructure.Documents.Reports;

namespace Flit.Infrastructure.Analytics.Scheduling;

/// <summary>
/// Reportes 2.0 (HU-D, tercera ola) — arma el adjunto del informe programado tipo "ot_operativo":
/// el panel propio del organismo de tránsito (<see cref="IOtMetricsReadRepository"/>, eje
/// invertido). NO confundir con <see cref="OtReportDocumentBuilder"/>, que arma el informe "ot" del
/// lado de la EMPRESA gestora auditando varios organismos — este vive del lado del organismo,
/// mirando hacia las empresas que le radican.
/// </summary>
internal sealed class OtOwnReportDocumentBuilder(IOtMetricsReadRepository repo)
{
    private static readonly CultureInfo Es = CultureInfo.InvariantCulture;

    public async Task<byte[]?> BuildAsync(
        Guid otTenantId, DateOnly from, DateOnly to, string format, CancellationToken ct)
    {
        var filter = new OtMetricsFilter(from, to);

        var panel = await repo.GetOperationalPanelAsync(otTenantId, filter, cancellationToken: ct)
            .ConfigureAwait(false);
        if (panel is null)
            return null; // Tenant sin organismo asociado: mismo criterio "sin adjunto" del resto del scheduler.

        var performance = await repo.GetPerformanceAsync(otTenantId, filter, cancellationToken: ct)
            .ConfigureAwait(false);
        var rejections = await repo.GetRejectionReasonsAsync(otTenantId, filter, cancellationToken: ct)
            .ConfigureAwait(false);

        return format == "pdf"
            ? BuildPdf(from, to, panel, performance, rejections)
            : BuildExcel(panel, performance, rejections);
    }

    private static byte[] BuildExcel(
        OtOperationalPanelDto panel, OtPerformanceDto? performance, OtRejectionReasonsDto? rejections)
    {
        var sheets = new List<TabularWorkbookWriter.Sheet>
        {
            TabularWorkbookWriter.Sheet.OfText(
                "Panel operativo",
                ["Entregados hoy", "Decididos hoy", "Pendientes", "Por revisar", "Esperando placa",
                    "En espera del cliente", "Hasta 1 día", "2-3 días", "4-7 días", "Más de 7 días",
                    "Prioritarios estancados"],
                (List<IReadOnlyList<string>>)
                    [[
                        panel.Movimiento.EntregadosHoy.ToString(Es), panel.Movimiento.DecididosHoy.ToString(Es),
                        panel.Movimiento.PendientesTotal.ToString(Es), panel.Cola.PorRevisar.ToString(Es),
                        panel.Cola.EsperandoAsignarPlaca.ToString(Es), panel.Cola.EnEsperaDelCliente.ToString(Es),
                        panel.Antiguedad.Hasta1Dia.ToString(Es), panel.Antiguedad.Entre2y3Dias.ToString(Es),
                        panel.Antiguedad.Entre4y7Dias.ToString(Es), panel.Antiguedad.MasDe7Dias.ToString(Es),
                        panel.Antiguedad.PrioritariosEstancados.ToString(Es),
                    ]]),
            TabularWorkbookWriter.Sheet.OfText(
                "Revisores",
                ["Revisor", "Decididos", "Aprobados", "Aprobación (%)", "Rechazados", "Rechazo (%)",
                    "Tiempo mediano (h)", "Vuelven a rechazarse (%)"],
                (performance?.Revisores ?? []).Select(r => (IReadOnlyList<string>)
                [
                    r.DisplayName, r.Decididos.ToString(Es), r.Aprobados.ToString(Es),
                    r.AprobacionPct.ToString("0.##", Es), r.Rechazados.ToString(Es),
                    r.RechazoPct.ToString("0.##", Es), Hours(r.TiempoMedianoHoras),
                    r.VuelvenARechazarsePct.ToString("0.##", Es),
                ]).ToList()),
            TabularWorkbookWriter.Sheet.OfText(
                "Calidad por empresa",
                ["Empresa", "Entregados", "Aprobados", "Pasan a la primera (%)", "Devoluciones promedio"],
                (performance?.Empresas ?? []).Select(e => (IReadOnlyList<string>)
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

    private static byte[] BuildPdf(
        DateOnly from, DateOnly to,
        OtOperationalPanelDto panel, OtPerformanceDto? performance, OtRejectionReasonsDto? rejections)
    {
        var periodLabel = $"{from:yyyy-MM-dd} a {to:yyyy-MM-dd}";
        var sections = new List<TabularReportPdfGenerator.Section>
        {
            new(
                "Panel operativo",
                ["Entregados hoy", "Decididos hoy", "Pendientes", "Más de 7 días", "Prioritarios estancados"],
                (List<IReadOnlyList<string>>)
                    [[
                        panel.Movimiento.EntregadosHoy.ToString(Es), panel.Movimiento.DecididosHoy.ToString(Es),
                        panel.Movimiento.PendientesTotal.ToString(Es), panel.Antiguedad.MasDe7Dias.ToString(Es),
                        panel.Antiguedad.PrioritariosEstancados.ToString(Es),
                    ]]),
            new(
                "Revisores",
                ["Revisor", "Decididos", "Aprobación (%)", "Rechazo (%)"],
                (performance?.Revisores ?? []).OrderByDescending(r => r.Decididos).Take(20)
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
            "Informe programado FLIT", "Mi organismo de tránsito", periodLabel, sections);
    }

    private static string Hours(double? h) => h is null ? "-" : h.Value.ToString("0.#", Es);
}
