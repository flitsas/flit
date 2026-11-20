using System.Globalization;
using System.Net;
using System.Text;
using Flit.Analytics.Application.Dtos;
using Flit.Modules.Security.Domain.Auth;
using Flit.Queries.Domain.Time;

namespace Flit.Infrastructure.Analytics.Scheduling;

/// <summary>
/// Plantillas HTML EN ESPAÑOL de los correos del scheduler (Reportes 2.0, HU-D). Paleta FLIT
/// (#557EFF primario / #162744 tinta). El cuerpo siempre lleva el resumen de KPIs de trámites
/// (mismo dato para los 5 tipos — es lo único que <see cref="Abstractions.IAnalyticsReadRepository"/>
/// resume sin filtrar por tipo); el archivo REAL del tipo (Excel/PDF) va adjunto — ver
/// <see cref="AnalyticsSchedulerProcessor.BuildAttachmentAsync"/>. Para "uso"/"ot", cuyos datos no
/// son trámites por categoría, el cuerpo lo aclara en vez de fingir que esa tabla les pertenece.
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
        IReadOnlyList<TopProducerDto> topProducers,
        EmailTheme? theme = null,
        string? assetsBaseUrl = null)
    {
        var subject = $"[FLIT] {scheduleName} — {periodLabel}";
        var html = theme is { IsBrand: true }
            ? BuildScheduledReportHtml(scheduleName, reportType, periodLabel, overview, topProducers, theme)
            : BuildScheduledReportFlit(scheduleName, reportType, periodLabel, overview, topProducers, assetsBaseUrl);
        return (subject, html);
    }

    /// <summary>Cuerpo del informe programado: KPIs por categoría + top de radicadores del periodo.</summary>
    private static string BuildScheduledReportHtml(
        string scheduleName,
        string reportType,
        string periodLabel,
        IReadOnlyList<CategoryMetricsDto> overview,
        IReadOnlyList<TopProducerDto> topProducers,
        EmailTheme? theme)
    {
        var sb = new StringBuilder();
        OpenLayout(sb, "Informe programado FLIT", scheduleName, theme);
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 4px\"><strong>Tipo de informe:</strong> {WebUtility.HtmlEncode(ReportTypeLabel(reportType))}</p>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 16px\"><strong>Periodo:</strong> {WebUtility.HtmlEncode(periodLabel)}</p>");

        if (reportType is "uso" or "ot")
        {
            sb.Append(
                "<p style=\"margin:0 0 16px\">El detalle de este informe (\""
                + WebUtility.HtmlEncode(ReportTypeLabel(reportType))
                + "\") va en el archivo adjunto. Este correo agrega, además, el resumen general de "
                + "trámites del periodo:</p>");
        }

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

        sb.Append("<p style=\"margin:16px 0 0;font-size:12px;color:#6b7a94\">Revisa el archivo adjunto para el detalle completo del periodo.</p>");
        CloseLayout(sb, theme);
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
        TimeZoneInfo timeZone,
        EmailTheme? theme = null,
        string? assetsBaseUrl = null)
    {
        var subject = $"[FLIT] Alerta: {ruleName}";
        var html = theme is { IsBrand: true }
            ? BuildAlertHtml(ruleName, metric, @operator, threshold, value, windowMinutes, triggeredAtUtc, timeZone, theme)
            : FlitDocumento(assetsBaseUrl, "Alerta FLIT", ruleName, FlitEmailLayout.TarjetaDetalle(null, new (string, string)[]
            {
                ("Métrica:", WebUtility.HtmlEncode(MetricLabel(metric))),
                ("Condición:", $"{WebUtility.HtmlEncode(OperatorLabel(@operator))} {threshold.ToString("0.##", Es)}"),
                ("Valor observado:", $"<span style=\"color:{FlitEmailLayout.Accent};font-weight:700\">{value.ToString("0.##", Es)}</span>"),
                ("Ventana de evaluación:", $"últimos {windowMinutes.ToString(Es)} minutos"),
                ("Fecha del disparo:", $"{FormatoFecha.Instante(triggeredAtUtc)} (hora de Bogotá)"),
            }));
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
        TimeZoneInfo timeZone,
        EmailTheme? theme)
    {
        var sb = new StringBuilder();
        OpenLayout(sb, "Alerta FLIT", ruleName, theme);
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 4px\"><strong>Métrica:</strong> {WebUtility.HtmlEncode(MetricLabel(metric))}</p>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 4px\"><strong>Condición:</strong> {WebUtility.HtmlEncode(OperatorLabel(@operator))} {threshold.ToString("0.##", Es)}</p>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 4px\"><strong>Valor observado:</strong> <span style=\"color:{Primary};font-weight:700\">{value.ToString("0.##", Es)}</span></p>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 4px\"><strong>Ventana de evaluación:</strong> últimos {windowMinutes.ToString(Es)} minutos</p>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0\"><strong>Fecha del disparo:</strong> {FormatoFecha.Instante(triggeredAtUtc)} (hora de Bogotá)</p>");
        CloseLayout(sb, theme);
        return sb.ToString();
    }

    /// <summary>
    /// Asunto + cuerpo del informe programado tipo "consulta" (Reportes 2.0, HU-D, segunda ola):
    /// no comparte plantilla con <see cref="BuildScheduledReport"/> porque no hay overview/top
    /// radicadores que mostrar — el contenido REAL va en el Excel adjunto, este cuerpo solo dice
    /// qué consulta es y si se truncó por el tope de filas.
    /// </summary>
    public static (string Subject, string Html) BuildConsultaReport(
        string scheduleName, string queryName, int total, bool truncated, int rowCap, EmailTheme? theme = null,
        string? assetsBaseUrl = null)
    {
        var subject = $"[FLIT] {scheduleName}";
        if (theme is not { IsBrand: true })
            return (subject, BuildConsultaReportFlit(scheduleName, queryName, total, truncated, rowCap, assetsBaseUrl));

        var sb = new StringBuilder();
        OpenLayout(sb, "Informe programado FLIT · Consulta personalizada", scheduleName, theme);
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 4px\"><strong>Consulta:</strong> {WebUtility.HtmlEncode(queryName)}</p>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 16px\"><strong>Resultados:</strong> {total.ToString(Es)}</p>");
        sb.Append(
            "<p style=\"margin:0 0 16px\">El detalle va en el Excel adjunto. El rango de fechas de la "
            + "consulta es relativo (p. ej. \"últimos 7 días\"), así que cada envío trae el periodo "
            + "actual, no el que tenía cuando se guardó.</p>");
        if (truncated)
        {
            sb.Append(
                $"<p style=\"margin:0 0 16px;color:#B45309\"><strong>Aviso:</strong> el adjunto trae las "
                + $"primeras {rowCap.ToString(Es)} de {total.ToString(Es)} filas. Entra a Consultas en la "
                + "app para ver o exportar el resto.</p>");
        }

        CloseLayout(sb, theme);
        return (subject, sb.ToString());
    }

    /// <summary>
    /// Cuerpo del correo cuando la SavedQuery de un informe tipo "consulta" ya no existe (se borró
    /// después de programar el informe) — se avisa en vez de enviar un correo vacío sin explicación.
    /// </summary>
    public static (string Subject, string Html) BuildConsultaReportMissing(
        string scheduleName, EmailTheme? theme = null, string? assetsBaseUrl = null)
    {
        var subject = $"[FLIT] {scheduleName} — consulta no disponible";
        if (theme is not { IsBrand: true })
        {
            return (subject, FlitDocumento(
                assetsBaseUrl,
                "Informe programado FLIT · Consulta personalizada",
                scheduleName,
                FlitEmailLayout.Parrafo(ConsultaNoDisponibleTexto, centrado: false, margen: "0")));
        }

        var sb = new StringBuilder();
        OpenLayout(sb, "Informe programado FLIT · Consulta personalizada", scheduleName, theme);
        sb.Append("<p style=\"margin:0\">" + ConsultaNoDisponibleTexto + "</p>");
        CloseLayout(sb, theme);
        return (subject, sb.ToString());
    }

    private const string ConsultaNoDisponibleTexto =
        "La consulta guardada que alimentaba este informe ya no existe "
        + "(se borró después de programarlo). Este informe no traerá adjunto hasta que se vuelva a "
        + "programar sobre una consulta guardada vigente.";

    // ── Correos 2.0: tema FLIT sobre FlitEmailLayout. La marca sigue en Open/CloseLayout. ──────

    /// <summary>
    /// Banner + antetítulo (<paramref name="kicker"/>) + título + cuerpo + pie de aviso automático.
    /// <paramref name="assetsBaseUrl"/> es <c>Notifications:EmailAssets:BaseUrl</c>; sin ella, el
    /// respaldo local de <see cref="FlitEmailLayout"/> (Bug #13194).
    /// </summary>
    private static string FlitDocumento(string? assetsBaseUrl, string kicker, string title, string cuerpoHtml) =>
        FlitEmailLayout.Documento(
            assetsBaseUrl,
            FlitEmailLayout.Titulo(title, WebUtility.HtmlEncode(kicker)),
            cuerpoHtml,
            FlitEmailLayout.PieFlit(
                assetsBaseUrl,
                $"Mensaje automático de {WebUtility.HtmlEncode("FLIT")} — no responda a este correo.",
                politicaPrivacidad: false));

    private static string BuildScheduledReportFlit(
        string scheduleName,
        string reportType,
        string periodLabel,
        IReadOnlyList<CategoryMetricsDto> overview,
        IReadOnlyList<TopProducerDto> topProducers,
        string? assetsBaseUrl)
    {
        var sb = new StringBuilder();
        sb.Append(FlitEmailLayout.TarjetaDetalle(null, new (string, string)[]
        {
            ("Tipo de informe:", WebUtility.HtmlEncode(ReportTypeLabel(reportType))),
            ("Periodo:", WebUtility.HtmlEncode(periodLabel)),
        }));

        if (reportType is "uso" or "ot")
        {
            sb.Append(FlitEmailLayout.Parrafo(
                "El detalle de este informe (\""
                + WebUtility.HtmlEncode(ReportTypeLabel(reportType))
                + "\") va en el archivo adjunto. Este correo agrega, además, el resumen general de "
                + "trámites del periodo:",
                centrado: false));
        }

        sb.Append(FlitSubtitulo("Trámites por categoría"));
        if (overview.Count == 0)
        {
            sb.Append(FlitEmailLayout.Parrafo("Sin actividad registrada en el periodo.", centrado: false));
        }
        else
        {
            sb.Append(FlitTablaInicio(("Categoría", "left"), ("Total", "right"), ("Por estado", "left")));
            foreach (var cat in overview)
            {
                var byStatus = string.Join(" · ",
                    cat.ByStatus.Select(s => $"{WebUtility.HtmlEncode(s.Status)}: {s.Count.ToString(Es)}"));
                sb.Append(FlitFila(
                    (WebUtility.HtmlEncode(cat.Category), "left"), (cat.Total.ToString(Es), "right"), (byStatus, "left")));
            }

            sb.Append("</table>");
        }

        sb.Append(FlitSubtitulo("Top radicadores del periodo"));
        if (topProducers.Count == 0)
        {
            sb.Append(FlitEmailLayout.Parrafo("Sin radicaciones en el periodo.", centrado: false));
        }
        else
        {
            sb.Append(FlitTablaInicio(("Usuario", "left"), ("Entregados", "right"), ("Aprobados", "right"), ("Rechazados", "right")));
            foreach (var p in topProducers)
            {
                sb.Append(FlitFila(
                    (WebUtility.HtmlEncode(p.DisplayName), "left"),
                    (p.SubmittedCount.ToString(Es), "right"),
                    (p.ApprovedCount.ToString(Es), "right"),
                    (p.RejectedCount.ToString(Es), "right")));
            }

            sb.Append("</table>");
        }

        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:16px 0 0;font-size:12px;color:{FlitEmailLayout.Muted};text-align:left;\">Revisa el archivo adjunto para el detalle completo del periodo.</p>");
        return FlitDocumento(assetsBaseUrl, "Informe programado FLIT", scheduleName, sb.ToString());
    }

    private static string BuildConsultaReportFlit(string scheduleName, string queryName, int total, bool truncated, int rowCap, string? assetsBaseUrl)
    {
        var sb = new StringBuilder();
        sb.Append(FlitEmailLayout.TarjetaDetalle(null, new (string, string)[]
        {
            ("Consulta:", WebUtility.HtmlEncode(queryName)),
            ("Resultados:", total.ToString(Es)),
        }));
        sb.Append(FlitEmailLayout.Parrafo(
            "El detalle va en el Excel adjunto. El rango de fechas de la "
            + "consulta es relativo (p. ej. \"últimos 7 días\"), así que cada envío trae el periodo "
            + "actual, no el que tenía cuando se guardó.",
            centrado: false));
        if (truncated)
        {
            sb.Append(FlitEmailLayout.Parrafo(
                "<span style=\"color:#B45309\"><strong>Aviso:</strong> el adjunto trae las "
                + $"primeras {rowCap.ToString(Es)} de {total.ToString(Es)} filas. Entra a Consultas en la "
                + "app para ver o exportar el resto.</span>",
                centrado: false));
        }

        return FlitDocumento(assetsBaseUrl, "Informe programado FLIT · Consulta personalizada", scheduleName, sb.ToString());
    }

    /// <summary>Subtítulo de sección; <paramref name="textoHtml"/> es un literal del composer (igual que antes, sin codificar).</summary>
    private static string FlitSubtitulo(string textoHtml) =>
        $"<h3 style=\"margin:20px 0 8px;font-size:16px;color:{FlitEmailLayout.Ink};text-align:left;\">{textoHtml}</h3>";

    /// <summary>Tabla de datos con encabezado en la tinta del kit; contenedor con scroll propio si no cabe.</summary>
    private static string FlitTablaInicio(params (string Texto, string Align)[] columnas)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"<table role=\"presentation\" cellpadding=\"6\" cellspacing=\"0\" border=\"0\" style=\"border-collapse:collapse;width:100%;font-size:13px;margin:0 0 8px;border:1px solid {FlitEmailLayout.CardBorder};\">");
        sb.Append(CultureInfo.InvariantCulture, $"<tr style=\"background:{FlitEmailLayout.Ink};color:#ffffff\">");
        foreach (var (texto, align) in columnas)
            sb.Append(CultureInfo.InvariantCulture, $"<th align=\"{align}\" style=\"font-size:11px;font-weight:600;\">{texto}</th>");
        sb.Append("</tr>");
        return sb.ToString();
    }

    private static string FlitFila(params (string Html, string Align)[] celdas)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"<tr style=\"border-bottom:1px solid {FlitEmailLayout.CardRowBorder}\">");
        foreach (var (html, align) in celdas)
            sb.Append(CultureInfo.InvariantCulture, $"<td align=\"{align}\" style=\"color:{FlitEmailLayout.Ink};word-break:break-word;\">{html}</td>");
        sb.Append("</tr>");
        return sb.ToString();
    }

    /// <summary>Mensaje plano en español que se persiste en <c>alert_events.message</c>.</summary>
    public static string BuildAlertMessage(
        string metric, string @operator, decimal threshold, decimal value, int windowMinutes) =>
        $"La métrica '{MetricLabel(metric)}' registró {value.ToString("0.##", Es)} " +
        $"({OperatorLabel(@operator)} {threshold.ToString("0.##", Es)}) " +
        $"en la ventana de los últimos {windowMinutes.ToString(Es)} minutos.";

    private static void OpenLayout(StringBuilder sb, string kicker, string title, EmailTheme? theme = null)
    {
        // HU #12428 AC2/AC9 — theme null/Flit: SIN CAMBIOS respecto al HTML anterior a esta
        // historia (parámetro aditivo, paridad byte a byte). Brand: color principal de la banda +
        // logotipo del tema si hay URL.
        var band = theme is { IsBrand: true } ? BrandedEmailChrome.LinkColor(theme) : Ink;
        var accent = theme is { IsBrand: true } ? BrandedEmailChrome.LinkColor(theme) : Primary;
        sb.Append(CultureInfo.InvariantCulture,
            $"<div style=\"font-family:Segoe UI,Arial,sans-serif;color:{Ink};max-width:640px;margin:0 auto\">");
        sb.Append(CultureInfo.InvariantCulture,
            $"<div style=\"background:{band};border-radius:12px 12px 0 0;padding:16px 20px\">" +
            $"<p style=\"margin:0;color:{accent};font-size:12px;letter-spacing:1px;text-transform:uppercase\">{WebUtility.HtmlEncode(kicker)}</p>" +
            $"<h2 style=\"margin:4px 0 0;color:#ffffff\">{WebUtility.HtmlEncode(title)}</h2></div>");
        sb.Append("<div style=\"border:1px solid #e3e9f5;border-top:0;border-radius:0 0 12px 12px;padding:20px\">");
        if (theme is { IsBrand: true } && !string.IsNullOrWhiteSpace(theme.LogoUrl))
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"<p style=\"margin:0 0 12px;text-align:center\"><img src=\"{WebUtility.HtmlEncode(theme.LogoUrl)}\" alt=\"{WebUtility.HtmlEncode(theme.PlatformName)}\" height=\"32\" style=\"max-height:32px;width:auto;border:0;\"/></p>");
        }
    }

    private static void CloseLayout(StringBuilder sb, EmailTheme? theme = null)
    {
        var platformName = theme is { IsBrand: true } ? theme.PlatformName : "FLIT";
        sb.Append("</div>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"font-size:11px;color:#6b7a94;margin:12px 0 0;text-align:center\">Mensaje automático de {WebUtility.HtmlEncode(platformName)} — no responda a este correo.</p>");
        sb.Append("</div>");
    }
}
