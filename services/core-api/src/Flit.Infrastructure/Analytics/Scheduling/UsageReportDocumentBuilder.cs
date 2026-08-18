using System.Globalization;
using Flit.Analytics.Application.Abstractions;
using Flit.Infrastructure.Documents.Reports;

namespace Flit.Infrastructure.Analytics.Scheduling;

/// <summary>
/// Reportes 2.0 (HU-D) — arma el adjunto del informe programado tipo "uso": las 4 colecciones que
/// alimentan la pestaña "Uso del aplicativo" (<see cref="IUsageMetricsReadRepository"/>), sin
/// tocar la pestaña "Resumen" (que usa <c>IAnalyticsReadRepository</c> y ya tenía su propio export).
/// Antes de esto, un informe programado tipo "uso" adjuntaba el mismo Excel/PDF de trámites que
/// "resumen" — dato que no tiene nada que ver con lo que el usuario pidió ver.
/// </summary>
internal sealed class UsageReportDocumentBuilder(IUsageMetricsReadRepository repo)
{
    private static readonly CultureInfo Es = CultureInfo.InvariantCulture;

    public async Task<byte[]> BuildAsync(
        Guid tenantId, DateOnly from, DateOnly to, string format, CancellationToken ct)
    {
        var wizardSteps = await repo.GetWizardStepMetricsAsync(tenantId, from, to, ct);
        var moduleUsage = await repo.GetModuleUsageAsync(tenantId, from, to, ct);
        var peakHours = await repo.GetPeakHoursAsync(tenantId, from, to, ct);
        var duration = await repo.GetWizardDurationAsync(tenantId, from, to, ct);

        return format == "pdf"
            ? BuildPdf(from, to, wizardSteps, moduleUsage, peakHours, duration)
            : BuildExcel(wizardSteps, moduleUsage, peakHours, duration);
    }

    private static byte[] BuildExcel(
        IReadOnlyList<WizardStepMetricDto> wizardSteps,
        IReadOnlyList<ModuleUsageDto> moduleUsage,
        IReadOnlyList<PeakHourDto> peakHours,
        WizardDurationDto duration)
    {
        var sheets = new List<TabularWorkbookWriter.Sheet>
        {
            TabularWorkbookWriter.Sheet.OfText(
                "Pasos del wizard",
                ["Paso", "Vistas", "Completados", "Abandono (%)", "Duración prom. (s)", "Duración mediana (s)"],
                wizardSteps.Select(s => (IReadOnlyList<string>)
                [
                    s.StepKey,
                    s.Views.ToString(Es),
                    s.Completions.ToString(Es),
                    s.AbandonmentPct.ToString("0.##", Es),
                    Seconds(s.AvgDurationMs),
                    Seconds(s.MedianDurationMs),
                ]).ToList()),
            TabularWorkbookWriter.Sheet.OfText(
                "Uso por módulo",
                ["Módulo", "Eventos", "Usuarios únicos"],
                moduleUsage.Select(m => (IReadOnlyList<string>)
                    [m.Module, m.Events.ToString(Es), m.UniqueUsers.ToString(Es)]).ToList()),
            TabularWorkbookWriter.Sheet.OfText(
                "Horas pico",
                ["Día (0=domingo)", "Hora", "Eventos"],
                peakHours.Select(p => (IReadOnlyList<string>)
                    [p.DayOfWeek.ToString(Es), p.Hour.ToString(Es), p.Events.ToString(Es)]).ToList()),
            TabularWorkbookWriter.Sheet.OfText(
                "Duración total del wizard",
                ["Duración promedio (s)", "Duración mediana (s)"],
                (List<IReadOnlyList<string>>)
                    [[Seconds(duration.AvgDurationMs), Seconds(duration.MedianDurationMs)]]),
        };

        return TabularWorkbookWriter.Write(sheets);
    }

    private static byte[] BuildPdf(
        DateOnly from,
        DateOnly to,
        IReadOnlyList<WizardStepMetricDto> wizardSteps,
        IReadOnlyList<ModuleUsageDto> moduleUsage,
        IReadOnlyList<PeakHourDto> peakHours,
        WizardDurationDto duration)
    {
        var periodLabel = $"{from:yyyy-MM-dd} a {to:yyyy-MM-dd}";
        var sections = new List<TabularReportPdfGenerator.Section>
        {
            new(
                "Pasos del wizard",
                ["Paso", "Vistas", "Completados", "Abandono (%)", "Duración prom.", "Duración mediana"],
                wizardSteps.Select(s => (IReadOnlyList<string>)
                [
                    s.StepKey,
                    s.Views.ToString(Es),
                    s.Completions.ToString(Es),
                    s.AbandonmentPct.ToString("0.##", Es),
                    Seconds(s.AvgDurationMs),
                    Seconds(s.MedianDurationMs),
                ]).ToList()),
            new(
                "Uso por módulo",
                ["Módulo", "Eventos", "Usuarios únicos"],
                moduleUsage.Select(m => (IReadOnlyList<string>)
                    [m.Module, m.Events.ToString(Es), m.UniqueUsers.ToString(Es)]).ToList()),
            new(
                "Horas pico (top 20)",
                ["Día (0=domingo)", "Hora", "Eventos"],
                peakHours.OrderByDescending(p => p.Events).Take(20)
                    .Select(p => (IReadOnlyList<string>)
                        [p.DayOfWeek.ToString(Es), p.Hour.ToString(Es), p.Events.ToString(Es)]).ToList()),
            new(
                "Duración total del wizard",
                ["Duración promedio", "Duración mediana"],
                (List<IReadOnlyList<string>>)
                    [[Seconds(duration.AvgDurationMs), Seconds(duration.MedianDurationMs)]]),
        };

        return TabularReportPdfGenerator.Generate(
            "Informe programado FLIT", "Uso del aplicativo", periodLabel, sections);
    }

    private static string Seconds(double? ms) => ms is null ? "-" : (ms.Value / 1000).ToString("0.#", Es);
}
