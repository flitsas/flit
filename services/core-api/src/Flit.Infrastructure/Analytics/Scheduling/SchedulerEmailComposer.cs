using System.Globalization;
using System.Net;
using System.Text;
using Flit.Analytics.Application.Dtos;

namespace Flit.Infrastructure.Analytics.Scheduling;

/// <summary>
/// Plantillas HTML EN ESPAÑOL de los correos del scheduler (Reportes 2.0, HU-D). Paleta FLIT
/// (#557EFF primario / #162744 tinta). <c>IEmailSender</c> solo soporta HtmlBody (sin adjuntos),
/// por eso el informe viaja como RESUMEN de KPIs en el cuerpo — limitación documentada en §8
/// del contrato (el archivo excel/pdf adjunto queda como mejora futura).
/// </summary>
internal static class SchedulerEmailComposer
{
    private const string Primary = "#557EFF";
    private const string Ink = "#162744";

    // El repo compila con InvariantGlobalization=true: es-CO no existe en runtime, así que los
    // números/fechas se formatean con InvariantCulture (punto decimal, dd/MM/yyyy explícito).
    private static readonly CultureInfo Es = CultureInfo.InvariantCulture;

    private static readonly Dictionary<string, string> ReportTypeLabels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["resumen"] = "Resumen general",
            ["operacion"] = "Operación / Trámites",
            ["ot"] = "Organismo de Tránsito",
            ["uso"] = "Uso del aplicativo",
            ["productividad"] = "Productividad",
        };

    private static readonly Dictionary<string, string> MetricLabels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["rejection_rate_pct"] = "Tasa de rechazo (%)",
            ["stuck_count"] = "Trámites atascados",
            ["external_api_errors"] = "Errores de integraciones externas",
            ["pending_identity_validations"] = "Validaciones de identidad pendientes",
            ["ict_stuck_in_validation"] = "ICT · Pre-trámites atascados en validación",
            ["ict_novelty_rate_pct"] = "ICT · Tasa de novedades (%)",
            ["ict_webhook_delivery_failures"] = "ICT · Fallos de entrega de webhook",
            ["ict_jobs_out_of_sla"] = "ICT · Jobs fuera de SLA",
        };

    private static readonly Dictionary<string, string> OperatorLabels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["gt"] = "mayor que",
            ["gte"] = "mayor o igual que",
            ["lt"] = "menor que",
            ["lte"] = "menor o igual que",
        };

    public static string ReportTypeLabel(string reportType) =>
        ReportTypeLabels.TryGetValue(reportType, out var label) ? label : reportType;

    public static string MetricLabel(string metric) =>
        MetricLabels.TryGetValue(metric, out var label) ? label : metric;

    public static string OperatorLabel(string @operator) =>
        OperatorLabels.TryGetValue(@operator, out var label) ? label : @operator;

    /// <summary>
    /// Asunto + cuerpo del informe programado (HU #11352 — antes el asunto lo interpolaba
    /// <c>AnalyticsSchedulerProcessor</c>; ahora la plantilla completa vive en un solo lugar).
    /// </summary>
    public static (string Subject, string Html) BuildScheduledReport(
        string scheduleName,
        string reportType,
        string periodLabel,
        IReadOnlyList<CategoryMetricsDto> overview,
        IReadOnlyList<TopProducerDto> topProducers)
    {
        var subject = $"[FLIT] {scheduleName} — {periodLabel}";
        var html = BuildScheduledReportHtml(scheduleName, reportType, periodLabel, overview, topProducers);
        return (subject, html);
    }

    /// <summary>Cuerpo del informe programado: KPIs por categoría + top de radicadores del periodo.</summary>
    private static string BuildScheduledReportHtml(
        string scheduleName,
        string reportType,
        string periodLabel,
        IReadOnlyList<CategoryMetricsDto> overview,
        IReadOnlyList<TopProducerDto> topProducers)
    {
        var sb = new StringBuilder();
        OpenLayout(sb, "Informe programado FLIT", scheduleName);
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 4px\"><strong>Tipo de informe:</strong> {WebUtility.HtmlEncode(ReportTypeLabel(reportType))}</p>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 16px\"><strong>Periodo:</strong> {WebUtility.HtmlEncode(periodLabel)}</p>");

        sb.Append(CultureInfo.InvariantCulture, $"<h3 style=\"color:{Ink};margin:16px 0 8px\">Trámites por categoría</h3>");
        if (overview.Count == 0)
        {
            sb.Append("<p style=\"margin:0 0 12px\">Sin actividad registrada en el periodo.</p>");
        }
        else
        {
            sb.Append("<table role=\"presentation\" cellpadding=\"6\" cellspacing=\"0\" style=\"border-collapse:collapse;width:100%\">");
            sb.Append(CultureInfo.InvariantCulture,
                $"<tr style=\"background:{Primary};color:#ffffff\"><th align=\"left\">Categoría</th><th align=\"right\">Total</th><th align=\"left\">Por estado</th></tr>");
            foreach (var cat in overview)
            {
                var byStatus = string.Join(" · ",
                    cat.ByStatus.Select(s => $"{WebUtility.HtmlEncode(s.Status)}: {s.Count.ToString(Es)}"));
                sb.Append(CultureInfo.InvariantCulture,
                    $"<tr style=\"border-bottom:1px solid #e3e9f5\"><td>{WebUtility.HtmlEncode(cat.Category)}</td><td align=\"right\">{cat.Total.ToString(Es)}</td><td>{byStatus}</td></tr>");
            }

            sb.Append("</table>");
        }

        sb.Append(CultureInfo.InvariantCulture, $"<h3 style=\"color:{Ink};margin:16px 0 8px\">Top radicadores del periodo</h3>");
        if (topProducers.Count == 0)
        {
            sb.Append("<p style=\"margin:0\">Sin radicaciones en el periodo.</p>");
        }
        else
        {
            sb.Append("<table role=\"presentation\" cellpadding=\"6\" cellspacing=\"0\" style=\"border-collapse:collapse;width:100%\">");
            sb.Append(CultureInfo.InvariantCulture,
                $"<tr style=\"background:{Primary};color:#ffffff\"><th align=\"left\">Usuario</th><th align=\"right\">Entregados</th><th align=\"right\">Aprobados</th><th align=\"right\">Rechazados</th></tr>");
            foreach (var p in topProducers)
            {
                sb.Append(CultureInfo.InvariantCulture,
                    $"<tr style=\"border-bottom:1px solid #e3e9f5\"><td>{WebUtility.HtmlEncode(p.DisplayName)}</td><td align=\"right\">{p.SubmittedCount.ToString(Es)}</td><td align=\"right\">{p.ApprovedCount.ToString(Es)}</td><td align=\"right\">{p.RejectedCount.ToString(Es)}</td></tr>");
            }

            sb.Append("</table>");
        }

        sb.Append("<p style=\"margin:16px 0 0;font-size:12px;color:#6b7a94\">El archivo adjunto (Excel/PDF) no está disponible por este canal; este correo incluye el resumen de indicadores del periodo.</p>");
        CloseLayout(sb);
        return sb.ToString();
    }

    /// <summary>
    /// Asunto + cuerpo del correo de alerta (HU #11352 — mismo traslado que el informe
    /// programado: el asunto deja de interpolarse en <c>AnalyticsSchedulerProcessor</c>).
    /// </summary>
    public static (string Subject, string Html) BuildAlert(
        string ruleName,
        string metric,
        string @operator,
        decimal threshold,
        decimal value,
        int windowMinutes,
        DateTimeOffset triggeredAtUtc,
        TimeZoneInfo timeZone)
    {
        var subject = $"[FLIT] Alerta: {ruleName}";
        var html = BuildAlertHtml(
            ruleName, metric, @operator, threshold, value, windowMinutes, triggeredAtUtc, timeZone);
        return (subject, html);
    }

    /// <summary>Cuerpo del correo de alerta: métrica, umbral, valor observado y ventana.</summary>
    private static string BuildAlertHtml(
        string ruleName,
        string metric,
        string @operator,
        decimal threshold,
        decimal value,
        int windowMinutes,
        DateTimeOffset triggeredAtUtc,
        TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTime(triggeredAtUtc, timeZone);
        var sb = new StringBuilder();
        OpenLayout(sb, "Alerta FLIT", ruleName);
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 4px\"><strong>Métrica:</strong> {WebUtility.HtmlEncode(MetricLabel(metric))}</p>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 4px\"><strong>Condición:</strong> {WebUtility.HtmlEncode(OperatorLabel(@operator))} {threshold.ToString("0.##", Es)}</p>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 4px\"><strong>Valor observado:</strong> <span style=\"color:{Primary};font-weight:700\">{value.ToString("0.##", Es)}</span></p>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 4px\"><strong>Ventana de evaluación:</strong> últimos {windowMinutes.ToString(Es)} minutos</p>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0\"><strong>Fecha del disparo:</strong> {local.ToString("dd/MM/yyyy HH:mm", Es)} (hora de Bogotá)</p>");
        CloseLayout(sb);
        return sb.ToString();
    }

    /// <summary>Mensaje plano en español que se persiste en <c>alert_events.message</c>.</summary>
    public static string BuildAlertMessage(
        string metric, string @operator, decimal threshold, decimal value, int windowMinutes) =>
        $"La métrica '{MetricLabel(metric)}' registró {value.ToString("0.##", Es)} " +
        $"({OperatorLabel(@operator)} {threshold.ToString("0.##", Es)}) " +
        $"en la ventana de los últimos {windowMinutes.ToString(Es)} minutos.";

    private static void OpenLayout(StringBuilder sb, string kicker, string title)
    {
        sb.Append(CultureInfo.InvariantCulture,
            $"<div style=\"font-family:Segoe UI,Arial,sans-serif;color:{Ink};max-width:640px;margin:0 auto\">");
        sb.Append(CultureInfo.InvariantCulture,
            $"<div style=\"background:{Ink};border-radius:12px 12px 0 0;padding:16px 20px\">" +
            $"<p style=\"margin:0;color:{Primary};font-size:12px;letter-spacing:1px;text-transform:uppercase\">{WebUtility.HtmlEncode(kicker)}</p>" +
            $"<h2 style=\"margin:4px 0 0;color:#ffffff\">{WebUtility.HtmlEncode(title)}</h2></div>");
        sb.Append("<div style=\"border:1px solid #e3e9f5;border-top:0;border-radius:0 0 12px 12px;padding:20px\">");
    }

    private static void CloseLayout(StringBuilder sb)
    {
        sb.Append("</div>");
        sb.Append("<p style=\"font-size:11px;color:#6b7a94;margin:12px 0 0;text-align:center\">Mensaje automático de FLIT — no responda a este correo.</p>");
        sb.Append("</div>");
    }
}
