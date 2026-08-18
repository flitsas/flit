using System.Globalization;
using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Queries.Metrics;
using Flit.Infrastructure.Documents.Reports;

namespace Flit.Infrastructure.Analytics.Scheduling;

/// <summary>
/// Reportes 2.0 (HU-D) — arma el adjunto del informe programado tipo "ot": las colecciones que
/// alimentan la pestaña "Organismo de Tránsito" de Reportes empresa
/// (<see cref="IAnalyticsMetricsReadRepository.GetOtMetricsAsync"/>). NO confundir con las métricas
/// del hub propio del organismo (<c>Flit.Admin.Domain.OtMetrics</c>, dominio distinto): este tipo de
/// informe vive en el lado de la empresa gestora, igual que "resumen"/"operación"/"productividad".
/// </summary>
internal sealed class OtReportDocumentBuilder(IAnalyticsMetricsReadRepository repo)
{
    private static readonly CultureInfo Es = CultureInfo.InvariantCulture;

    public async Task<byte[]> BuildAsync(
        Guid tenantId, DateOnly from, DateOnly to, string format, CancellationToken ct)
    {
        var metrics = await repo.GetOtMetricsAsync(new MetricsFilter(tenantId, from, to), ct);

        return format == "pdf" ? BuildPdf(from, to, metrics) : BuildExcel(metrics);
    }

    private static byte[] BuildExcel(OtMetricsDto m)
    {
        var s = m.Summary;
        var sheets = new List<TabularWorkbookWriter.Sheet>
        {
            new(
                "Resumen",
                ["Entregados", "Aprobados", "Rechazados", "Tasa de rechazo (%)", "Horas prom. aprobación",
                    "P50 horas", "P90 horas", "Reincidencia (%)", "Atascados"],
                (List<IReadOnlyList<string>>)
                    [[
                        s.Entregados.ToString(Es), s.Aprobados.ToString(Es), s.Rechazados.ToString(Es),
                        s.RejectionRatePct.ToString("0.##", Es), Hours(s.AvgApprovalHours), Hours(s.P50ApprovalHours),
                        Hours(s.P90ApprovalHours), s.ReincidencePct.ToString("0.##", Es), s.StuckCount.ToString(Es),
                    ]]),
            new(
                "Rechazo por organismo",
                ["Organismo", "Entregados", "Aprobados", "Rechazados", "Tasa de rechazo (%)"],
                m.RejectionByOffice.Select(r => (IReadOnlyList<string>)
                [
                    r.TransitOfficeName, r.Entregados.ToString(Es), r.Aprobados.ToString(Es),
                    r.Rechazados.ToString(Es), r.RejectionRatePct.ToString("0.##", Es),
                ]).ToList()),
            new(
                "Causales tipificadas",
                ["Código", "Descripción", "Rechazos", "% de rechazos que la incluyen"],
                m.RejectionByReasonCatalog.Select(r => (IReadOnlyList<string>)
                    [r.Code, r.Description, r.Rechazos.ToString(Es), r.Pct.ToString("0.##", Es)]).ToList()),
            new(
                "Causales en texto libre",
                ["Motivo", "Cantidad", "%"],
                m.RejectionByReason.Select(r => (IReadOnlyList<string>)
                    [r.Reason, r.Count.ToString(Es), r.Pct.ToString("0.##", Es)]).ToList()),
            new(
                "Rechazo por tipo de trámite",
                ["Tipo de trámite", "Entregados", "Rechazados", "Tasa de rechazo (%)"],
                m.RejectionByType.Select(r => (IReadOnlyList<string>)
                    [r.ProcedureTypeName, r.Entregados.ToString(Es), r.Rechazados.ToString(Es),
                        r.RejectionRatePct.ToString("0.##", Es)]).ToList()),
            new(
                "Tiempos de aprobación por organismo",
                ["Organismo", "Decididos", "Horas prom.", "P50 horas", "P90 horas"],
                m.ApprovalTimesByOffice.Select(a => (IReadOnlyList<string>)
                    [a.TransitOfficeName, a.Decididos.ToString(Es), Hours(a.AvgHours), Hours(a.P50Hours),
                        Hours(a.P90Hours)]).ToList()),
            new(
                "Ranking de agilidad",
                ["Puesto", "Organismo", "P50 horas", "Tasa de rechazo (%)", "Volumen"],
                m.OfficeRanking.Select(r => (IReadOnlyList<string>)
                    [r.Rank.ToString(Es), r.TransitOfficeName, r.P50Hours.ToString("0.#", Es),
                        r.RejectionRatePct.ToString("0.##", Es), r.Volumen.ToString(Es)]).ToList()),
            new(
                "Reincidencia y ciclo interno",
                ["Rechazadas", "Reintentadas", "Ciclos prom.", "Ciclos máx.", "Ciclo interno prom. (h)",
                    "Ciclo interno P50 (h)", "Ciclo interno P90 (h)"],
                (List<IReadOnlyList<string>>)
                    [[
                        m.Reincidence.Rechazadas.ToString(Es), m.Reincidence.Reintentadas.ToString(Es),
                        m.Reincidence.AvgCiclos.ToString("0.##", Es), m.Reincidence.MaxCiclos.ToString(Es),
                        Hours(m.InternalCycle.AvgHours), Hours(m.InternalCycle.P50Hours), Hours(m.InternalCycle.P90Hours),
                    ]]),
            new(
                $"Atascados (top {m.Stuck.Items.Count} de {m.Stuck.TotalCount})",
                ["Referencia", "Estado", "Días en el estado", "Organismo", "Tipo de trámite", "Radicado por"],
                m.Stuck.Items.Select(i => (IReadOnlyList<string>)
                [
                    i.ReferenceNumber, i.Status, i.DaysInStatus.ToString("0.#", Es),
                    i.TransitOfficeName ?? "-", i.ProcedureTypeName, i.CreatedByDisplayName,
                ]).ToList()),
        };

        return TabularWorkbookWriter.Write(sheets);
    }

    private static byte[] BuildPdf(DateOnly from, DateOnly to, OtMetricsDto m)
    {
        var periodLabel = $"{from:yyyy-MM-dd} a {to:yyyy-MM-dd}";
        var s = m.Summary;
        var sections = new List<TabularReportPdfGenerator.Section>
        {
            new(
                "Resumen",
                ["Entregados", "Aprobados", "Rechazados", "Tasa rechazo (%)", "P50 horas", "P90 horas",
                    "Reincidencia (%)", "Atascados"],
                (List<IReadOnlyList<string>>)
                    [[
                        s.Entregados.ToString(Es), s.Aprobados.ToString(Es), s.Rechazados.ToString(Es),
                        s.RejectionRatePct.ToString("0.##", Es), Hours(s.P50ApprovalHours), Hours(s.P90ApprovalHours),
                        s.ReincidencePct.ToString("0.##", Es), s.StuckCount.ToString(Es),
                    ]]),
            new(
                "Rechazo por organismo",
                ["Organismo", "Entregados", "Rechazados", "Tasa (%)"],
                m.RejectionByOffice.Select(r => (IReadOnlyList<string>)
                    [r.TransitOfficeName, r.Entregados.ToString(Es), r.Rechazados.ToString(Es),
                        r.RejectionRatePct.ToString("0.##", Es)]).ToList()),
            new(
                "Causales tipificadas más frecuentes",
                ["Código", "Descripción", "Rechazos", "% que la incluyen"],
                m.RejectionByReasonCatalog.OrderByDescending(r => r.Rechazos).Take(15)
                    .Select(r => (IReadOnlyList<string>)
                        [r.Code, r.Description, r.Rechazos.ToString(Es), r.Pct.ToString("0.##", Es)]).ToList()),
            new(
                "Ranking de agilidad",
                ["Puesto", "Organismo", "P50 horas", "Tasa rechazo (%)"],
                m.OfficeRanking.Select(r => (IReadOnlyList<string>)
                    [r.Rank.ToString(Es), r.TransitOfficeName, r.P50Hours.ToString("0.#", Es),
                        r.RejectionRatePct.ToString("0.##", Es)]).ToList()),
            new(
                $"Atascados (top {Math.Min(20, m.Stuck.Items.Count)} de {m.Stuck.TotalCount})",
                ["Referencia", "Estado", "Días", "Organismo"],
                m.Stuck.Items.Take(20).Select(i => (IReadOnlyList<string>)
                    [i.ReferenceNumber, i.Status, i.DaysInStatus.ToString("0.#", Es), i.TransitOfficeName ?? "-"])
                    .ToList()),
        };

        return TabularReportPdfGenerator.Generate(
            "Informe programado FLIT", "Organismo de Tránsito", periodLabel, sections);
    }

    private static string Hours(double? h) => h is null ? "-" : h.Value.ToString("0.#", Es);
}
