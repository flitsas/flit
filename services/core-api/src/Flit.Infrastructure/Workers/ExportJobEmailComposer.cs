using System.Globalization;
using System.Net;
using Flit.Modules.Security.Domain.Auth;

namespace Flit.Infrastructure.Workers;

/// <summary>Plantillas HTML de notificación de export jobs (HU #11107 / AC1 / AC6).</summary>
internal static class ExportJobEmailComposer
{
    private const string Primary = "#557EFF";
    private const string Ink = "#162744";

    public static EmailMessage BuildCompleted(
        string toEmail,
        string toName,
        Guid jobId,
        string reportType,
        string format) =>
        new(
            toEmail,
            toName,
            $"FLIT — Exportación lista ({reportType})",
            BuildBody(
                title: "Exportación completada",
                intro: "Tu archivo de reporte ya está disponible para descarga.",
                jobId,
                reportType,
                format,
                statusLabel: "Completada",
                statusColor: "#1B7F4E"));

    public static EmailMessage BuildFailed(
        string toEmail,
        string toName,
        Guid jobId,
        string reportType,
        string format,
        string? errorHint) =>
        new(
            toEmail,
            toName,
            $"FLIT — Exportación fallida ({reportType})",
            BuildBody(
                title: "Exportación fallida",
                intro: string.IsNullOrWhiteSpace(errorHint)
                    ? "No pudimos generar el archivo. Puedes reintentar desde Reportes."
                    : $"No pudimos generar el archivo: {WebUtility.HtmlEncode(errorHint)}",
                jobId,
                reportType,
                format,
                statusLabel: "Fallida",
                statusColor: "#B42318"));

    private static string BuildBody(
        string title,
        string intro,
        Guid jobId,
        string reportType,
        string format,
        string statusLabel,
        string statusColor)
    {
        var jobIdText = WebUtility.HtmlEncode(jobId.ToString("D", CultureInfo.InvariantCulture));
        var report = WebUtility.HtmlEncode(reportType);
        var fmt = WebUtility.HtmlEncode(format);
        return $"""
            <div style="font-family:Segoe UI,Arial,sans-serif;color:{Ink};max-width:560px">
              <h2 style="margin:0 0 12px;color:{Primary}">{WebUtility.HtmlEncode(title)}</h2>
              <p style="margin:0 0 16px">{intro}</p>
              <table style="border-collapse:collapse;width:100%;font-size:14px">
                <tr><td style="padding:6px 0;color:#667085">JobId</td><td style="padding:6px 0"><code>{jobIdText}</code></td></tr>
                <tr><td style="padding:6px 0;color:#667085">Reporte</td><td style="padding:6px 0">{report}</td></tr>
                <tr><td style="padding:6px 0;color:#667085">Formato</td><td style="padding:6px 0">{fmt}</td></tr>
                <tr><td style="padding:6px 0;color:#667085">Estado</td><td style="padding:6px 0;color:{statusColor};font-weight:600">{statusLabel}</td></tr>
              </table>
              <p style="margin:16px 0 0;font-size:12px;color:#667085">Puedes consultar el estado en Reportes → Exportaciones.</p>
            </div>
            """;
    }
}
