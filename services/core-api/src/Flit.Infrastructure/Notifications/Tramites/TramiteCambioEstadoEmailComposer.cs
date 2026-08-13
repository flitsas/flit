using System.Globalization;
using System.Net;
using System.Text;

namespace Flit.Infrastructure.Notifications.Tramites;

/// <summary>
/// Datos de las plantillas de trámite aprobado/rechazado.
/// </summary>
public sealed record TramiteCambioEstadoEmailModel(
    string VendedorNombre,
    string CompradorNombre,
    string Placa,
    string CiudadOt,
    string NombreOt,
    string EstadoActual,
    bool EsTraspaso,
    IReadOnlyList<string>? CausalesRechazo = null,
    string? ObservacionRechazo = null);

/// <summary>
/// Composer compartido de <c>tramites.aprobado</c> y <c>tramites.rechazado</c>:
/// variante FLIT (marca) y Renting (diseño Compra Tu Usado).
/// Disparador productivo: sink + worker (HU #11465 / #11467, ADR-0045).
/// </summary>
public static class TramiteCambioEstadoEmailComposer
{
    public const string TemplateIdAprobado = "tramites.aprobado";
    public const string TemplateIdRechazado = "tramites.rechazado";

    private const string PrimaryBlue = "#2F6FED";
    private const string Ink = "#162244";
    private const string ApprovedGreen = "#2E7D32";
    private const string RejectedRed = "#C62828";
    private const string RentingInk = "#1A1A1A";
    private const string RentingMuted = "#4A4A4A";
    private const string RentingLink = "#2F5BEA";
    private const string PqrsUrl = "https://usadosrentingcolombia.com";
    private const string RentingSupportEmail = "servicio@rentingcolombia.com";
    private const string PrivacyPolicyUrl = "https://flitsas.com/politica-de-privacidad";
    private const string SupportEmail = "soporte@flit.com";

    public const string DefaultHeaderFileName = "tramite-cambio-estado-header.png";
    public const string DefaultLogoFileName = "flit-logo.png";
    public const string DefaultRentingHeaderFileName = "tramite-cambio-estado-renting-header.png";
    public const string DefaultRentingFooterFileName = "tramite-cambio-estado-renting-footer.png";

    /// <summary>URL absoluta del banner FLIT (correo: debe ser HTTPS público).</summary>
    public static string ResolveHeaderUrl(string assetsBaseUrl) =>
        CombineAssetUrl(assetsBaseUrl, DefaultHeaderFileName);

    /// <summary>URL absoluta del logo FLIT (firma).</summary>
    public static string ResolveLogoUrl(string assetsBaseUrl) =>
        CombineAssetUrl(assetsBaseUrl, DefaultLogoFileName);

    /// <summary>URL absoluta del encabezado Renting (progreso + marca).</summary>
    public static string ResolveRentingHeaderUrl(string assetsBaseUrl) =>
        CombineAssetUrl(assetsBaseUrl, DefaultRentingHeaderFileName);

    /// <summary>URL absoluta del pie de contacto Renting.</summary>
    public static string ResolveRentingFooterUrl(string assetsBaseUrl) =>
        CombineAssetUrl(assetsBaseUrl, DefaultRentingFooterFileName);

    public static (string Subject, string Html) ComposeFlit(
        TramiteCambioEstadoEmailModel model, string assetsBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(model);
        var estado = NormalizeEstado(model.EstadoActual);
        var subject = BuildSubject(model.Placa, estado);
        var html = BuildFlitHtml(model, estado, assetsBaseUrl);
        return (subject, html);
    }

    public static (string Subject, string Html) ComposeRenting(
        TramiteCambioEstadoEmailModel model, string assetsBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(model);
        var estado = NormalizeEstado(model.EstadoActual);
        var subject = BuildSubject(model.Placa, estado);
        var html = BuildRentingHtml(model, estado, assetsBaseUrl);
        return (subject, html);
    }

    private static string BuildSubject(string placa, string estado) =>
        $"[FLIT] Notificación radicación del trámite — {placa} — {estado}";

    private static string NormalizeEstado(string estado)
    {
        if (string.Equals(estado, "APROBADO", StringComparison.OrdinalIgnoreCase))
            return "APROBADO";
        if (string.Equals(estado, "RECHAZADO", StringComparison.OrdinalIgnoreCase))
            return "RECHAZADO";
        return string.IsNullOrWhiteSpace(estado) ? "RECHAZADO" : estado.Trim().ToUpperInvariant();
    }

    private static bool IsApproved(string estado) =>
        string.Equals(estado, "APROBADO", StringComparison.Ordinal);

    private static string BuildFlitHtml(
        TramiteCambioEstadoEmailModel model, string estado, string assetsBaseUrl)
    {
        var comprador = Enc(model.CompradorNombre);
        var placa = Enc(model.Placa);
        var ciudad = Enc(model.CiudadOt);
        var ot = Enc(model.NombreOt);
        var approved = IsApproved(estado);
        var estadoColor = approved ? ApprovedGreen : RejectedRed;
        var estadoIcon = approved ? "✓" : "❌";
        var headerUrl = EncAttr(ResolveHeaderUrl(assetsBaseUrl));
        var logoUrl = EncAttr(ResolveLogoUrl(assetsBaseUrl));
        var estadoEnc = Enc(estado);

        var saludo = model.EsTraspaso
            ? $"Estimados Señor/a <strong style=\"color:{PrimaryBlue}\">{Enc(model.VendedorNombre)}</strong> y Señor/a <strong style=\"color:{PrimaryBlue}\">{comprador}</strong>."
            : $"Estimado/a Señor/a <strong style=\"color:{PrimaryBlue}\">{comprador}</strong>.";

        var introParte = model.EsTraspaso
            ? $"el traspaso de propiedad del vehículo con placa <strong>{placa}</strong>"
            : $"el trámite del vehículo con placa <strong>{placa}</strong>";

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"es\"><head><meta charset=\"utf-8\"/></head>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<body style=\"margin:0;padding:0;background:#ffffff;font-family:Arial,Helvetica,sans-serif;color:{Ink};\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:640px;margin:0 auto;background:#ffffff;\">");
        sb.Append(CultureInfo.InvariantCulture,
            $"<tr><td style=\"padding:0;\"><img src=\"{headerUrl}\" alt=\"FLIT Version 2.0\" width=\"640\" style=\"display:block;width:100%;max-width:640px;height:auto;border:0;\"/></td></tr>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<tr><td style=\"padding:28px 28px 8px;text-align:center;\"><h1 style=\"margin:0;font-size:20px;letter-spacing:0.02em;color:{PrimaryBlue};\">¡NOTIFICACIÓN RADICACIÓN DEL TRÁMITE!</h1></td></tr>");
        sb.Append("<tr><td style=\"padding:16px 28px 8px;font-size:14px;line-height:1.55;\">");
        sb.Append(CultureInfo.InvariantCulture, $"<p style=\"margin:0 0 12px;\">{saludo}</p>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 16px;\">Nos ponemos en contacto para informarle que {introParte} ha sido <strong style=\"color:{estadoColor};\">{estadoEnc}</strong>.</p>");
        if (model.EsTraspaso)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"<p style=\"margin:0 0 6px;\"><strong style=\"color:{PrimaryBlue};\">Vendedor:</strong> {Enc(model.VendedorNombre)}</p>");
        }
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 6px;\"><strong style=\"color:{PrimaryBlue};\">Comprador:</strong> {comprador}</p>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 6px;\"><strong style=\"color:{PrimaryBlue};\">Placa del Vehículo:</strong> {placa}</p>");
        if (!string.IsNullOrWhiteSpace(model.CiudadOt))
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"<p style=\"margin:0 0 6px;\"><strong style=\"color:{PrimaryBlue};\">Ciudad:</strong> {ciudad}</p>");
        }
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 6px;\"><strong style=\"color:{PrimaryBlue};\">Secretaría de Tránsito:</strong> {ot}</p>");
        var rejectionLines = BuildRejectionLines(model, estado);
        var estadoMargin = rejectionLines.Count > 0 ? "6px" : "16px";
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 {estadoMargin};\"><strong style=\"color:{PrimaryBlue};\">Estado Actual:</strong> <span style=\"color:{estadoColor};font-weight:700;\">{estadoIcon} {estadoEnc}</span></p>");
        AppendRejectionLines(
            sb,
            rejectionLines,
            lastMargin: "16px",
            (label, value, margin) =>
                $"<p style=\"margin:0 0 {margin};\"><strong style=\"color:{PrimaryBlue};\">{label}:</strong> {value}</p>");
        sb.Append("<p style=\"margin:0 0 20px;\">En FLIT, nos aseguramos de que los trámites de tránsito sean más ágiles, eficientes y sin contratiempos.</p>");
        sb.Append("<p style=\"margin:0 0 8px;\">Cordialmente,</p>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<img src=\"{logoUrl}\" alt=\"flit\" width=\"96\" style=\"display:block;border:0;margin:0 0 24px;\"/>");
        sb.Append("</td></tr>");
        sb.Append("<tr><td style=\"padding:0 28px 28px;\">");
        sb.Append("<hr style=\"border:none;border-top:1px solid #E5EAF2;margin:0 0 16px;\"/>");
        sb.Append("<p style=\"margin:0 0 12px;font-size:12px;line-height:1.5;color:#6B778C;text-align:center;\">");
        sb.Append("Si tienes alguna pregunta o necesitas ayuda, no dudes en ponerte en contacto con nosotros. ");
        sb.Append(CultureInfo.InvariantCulture,
            $"Puedes hacerlo a través de <strong>{Enc(SupportEmail)}</strong> o nuestro <strong>chat online</strong>.");
        sb.Append("</p>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0;text-align:center;\"><a href=\"{EncAttr(PrivacyPolicyUrl)}\" style=\"color:{PrimaryBlue};font-size:12px;text-decoration:underline;\">POLÍTICA DE PRIVACIDAD</a></p>");
        sb.Append("</td></tr></table></body></html>");
        return sb.ToString();
    }

    private static string BuildRentingHtml(
        TramiteCambioEstadoEmailModel model, string estado, string assetsBaseUrl)
    {
        var comprador = Enc(model.CompradorNombre);
        var placa = Enc(model.Placa);
        var ciudad = Enc(model.CiudadOt);
        var ot = Enc(model.NombreOt);
        var approved = IsApproved(estado);
        var estadoEnc = Enc(estado);
        var estadoColor = approved ? ApprovedGreen : RejectedRed;
        var headerUrl = EncAttr(ResolveRentingHeaderUrl(assetsBaseUrl));
        var footerUrl = EncAttr(ResolveRentingFooterUrl(assetsBaseUrl));
        var pqrsHref = EncAttr(PqrsUrl);
        var supportEmail = Enc(RentingSupportEmail);
        var supportMailto = EncAttr($"mailto:{RentingSupportEmail}");

        var procesoParte = model.EsTraspaso
            ? "del traspaso de propiedad del vehículo"
            : "del trámite del vehículo";

        var lead = approved
            ? $"¡Buenas Noticias! Queremos mantenerte informado/a sobre el estado actualizado {procesoParte} con placa <strong>{placa}</strong>"
            : $"Queremos mantenerte informado/a sobre el estado actualizado {procesoParte} con placa <strong>{placa}</strong>";

        var cierre = approved
            ? "Tu tarjeta de propiedad/matrícula llegará pronto. Estamos haciendo todo lo posible para agilizar la entrega de manera física."
            : "Estamos revisando el caso para orientarte en los siguientes pasos. Si ya tienes información adicional, comunícate con nosotros por los canales habilitados.";

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"es\"><head><meta charset=\"utf-8\"/>");
        sb.Append("<meta name=\"color-scheme\" content=\"light\"/>");
        sb.Append("<meta name=\"supported-color-schemes\" content=\"light\"/>");
        sb.Append("<style type=\"text/css\">");
        sb.Append(":root{color-scheme:light;}");
        sb.Append(".renting-header-bg{background-color:#ffffff !important;}");
        sb.Append(".renting-footer-bg{background-color:#ffffff !important;}");
        sb.Append(".renting-body-bg{background-color:#ffffff !important;}");
        sb.Append("@media (prefers-color-scheme:dark){.renting-header-bg,.renting-footer-bg,.renting-body-bg{background-color:#ffffff !important;}}");
        sb.Append("</style></head>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<body style=\"margin:0;padding:0;background:#ffffff;font-family:Arial,Helvetica,sans-serif;color:{RentingInk};\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:640px;margin:0 auto;background:#ffffff;\">");
        sb.Append(CultureInfo.InvariantCulture,
            $"<tr><td class=\"renting-header-bg\" bgcolor=\"#ffffff\" style=\"padding:0;background-color:#ffffff !important;\"><img src=\"{headerUrl}\" alt=\"Compra Tu Usado — Renting Colombia\" width=\"640\" style=\"display:block;width:100%;max-width:640px;height:auto;border:0;background-color:#ffffff;\"/></td></tr>");
        sb.Append("<tr><td class=\"renting-body-bg\" style=\"padding:28px 32px 8px;font-size:15px;line-height:1.55;background-color:#ffffff;\">");
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 16px;\"><strong>{comprador}</strong>, ¡Es un gusto saludarte!</p>");
        sb.Append(CultureInfo.InvariantCulture, $"<p style=\"margin:0 0 20px;\">{lead}</p>");
        sb.Append("<p style=\"margin:0 0 10px;\"><strong>Detalles clave:</strong></p>");
        if (model.EsTraspaso)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"<p style=\"margin:0 0 6px;\"><strong>Vendedor:</strong> {Enc(model.VendedorNombre)}</p>");
        }
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 6px;\"><strong>Comprador:</strong> {comprador}</p>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 6px;\"><strong>Placa del Vehículo:</strong> {placa}</p>");
        if (!string.IsNullOrWhiteSpace(model.CiudadOt))
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"<p style=\"margin:0 0 6px;\"><strong>Ciudad:</strong> {ciudad}</p>");
        }
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 6px;\"><strong>Secretaría de Tránsito:</strong> {ot}</p>");
        var rejectionLines = BuildRejectionLines(model, estado);
        var estadoMargin = rejectionLines.Count > 0 ? "6px" : "18px";
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 {estadoMargin};\"><strong>Estado Actual:</strong> <span style=\"color:{estadoColor};font-weight:700;\">{estadoEnc}</span></p>");
        AppendRejectionLines(
            sb,
            rejectionLines,
            lastMargin: "18px",
            (label, value, margin) =>
                $"<p style=\"margin:0 0 {margin};\"><strong>{label}:</strong> {value}</p>");
        sb.Append(CultureInfo.InvariantCulture, $"<p style=\"margin:0 0 18px;\">{cierre}</p>");
        sb.Append("<p style=\"margin:0 0 8px;\">Si tienes alguna inquietud, comunícate con nosotros:</p>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 6px;\"><strong>Formularios web:</strong> <a href=\"{pqrsHref}\" style=\"color:{RentingLink};text-decoration:underline;\">Formulario PQRS (usadosrentingcolombia.com)</a></p>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 18px;\"><strong>Correo electrónico:</strong> <a href=\"{supportMailto}\" style=\"color:{RentingInk};text-decoration:none;\">{supportEmail}</a></p>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 8px;font-size:13px;line-height:1.5;color:{RentingMuted};\">Por favor, <strong>NO</strong> respondas a este mensaje, es un medio informativo automático.</p>");
        sb.Append("</td></tr>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<tr><td class=\"renting-footer-bg\" bgcolor=\"#ffffff\" style=\"padding:8px 24px 28px;background-color:#ffffff !important;\"><img src=\"{footerUrl}\" alt=\"Línea nacional y WhatsApp Renting Colombia\" width=\"592\" style=\"display:block;width:100%;max-width:592px;height:auto;border:0;margin:0 auto;background-color:#ffffff;\"/></td></tr>");
        sb.Append("</table></body></html>");
        return sb.ToString();
    }

    private static string CombineAssetUrl(string assetsBaseUrl, string fileName)
    {
        var baseUrl = string.IsNullOrWhiteSpace(assetsBaseUrl)
            ? "https://dev.flitsas.online/email-assets"
            : assetsBaseUrl.TrimEnd('/');
        return $"{baseUrl}/{fileName}";
    }

    private static List<(string Label, string ValueHtml)> BuildRejectionLines(
        TramiteCambioEstadoEmailModel model, string estado)
    {
        if (!string.Equals(estado, "RECHAZADO", StringComparison.Ordinal))
            return [];

        var lines = new List<(string Label, string ValueHtml)>();
        var causales = (model.CausalesRechazo ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .ToList();
        if (causales.Count > 0)
            lines.Add(("Motivo de rechazo", Enc(string.Join("; ", causales))));

        var observacion = model.ObservacionRechazo?.Trim();
        if (!string.IsNullOrEmpty(observacion))
            lines.Add(("Observación", EncMultiline(observacion)));

        return lines;
    }

    private static void AppendRejectionLines(
        StringBuilder sb,
        List<(string Label, string ValueHtml)> lines,
        string lastMargin,
        Func<string, string, string, string> formatLine)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var margin = i == lines.Count - 1 ? lastMargin : "6px";
            var (label, value) = lines[i];
            sb.Append(formatLine(label, value, margin));
        }
    }

    private static string Enc(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }

    private static string EncMultiline(string value) =>
        Enc(value)
            .Replace("\r\n", "<br/>", StringComparison.Ordinal)
            .Replace("\n", "<br/>", StringComparison.Ordinal)
            .Replace("\r", "<br/>", StringComparison.Ordinal);

    private static string EncAttr(string? value) =>
        WebUtility.HtmlEncode(value ?? string.Empty).Replace("\"", "&quot;", StringComparison.Ordinal);
}
