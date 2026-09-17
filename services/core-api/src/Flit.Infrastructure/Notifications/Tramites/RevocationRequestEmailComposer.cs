using System.Globalization;
using System.Net;
using System.Text;
using Flit.Tramites.Domain.RevocationRequests;

namespace Flit.Infrastructure.Notifications.Tramites;

/// <summary>
/// Datos de las 3 plantillas del sub-flujo de revocatoria (HU #12579, Feature #12565).
/// </summary>
/// <param name="DestinatarioNombre">
/// Nombre del radicador (único destinatario hoy — ver <c>RevocationRequestNotificationEnqueuer</c>).
/// </param>
/// <param name="Motivo">
/// Motivo de rechazo (<c>ProcedureRevocationRequest.DecisionReason</c>). Solo se usa/muestra en
/// <see cref="RevocationRequestEmailMilestone.Rechazada"/>; se ignora en los otros dos hitos.
/// </param>
public sealed record RevocationRequestEmailModel(
    string DestinatarioNombre,
    string Placa,
    string Radicado,
    string? Motivo = null);

/// <summary>
/// Composer de <c>tramites.revocatoria-solicitada</c> / <c>-aprobada</c> / <c>-rechazada</c>:
/// variante FLIT y Renting (mismo criterio de marca por canal que
/// <see cref="TramiteCambioEstadoEmailComposer"/> y <c>AsignacionPlacaEmailComposer</c> — nunca
/// por NIT).
/// <para>
/// Reutiliza el chrome visual (banner/logo FLIT, banner/footer Renting) de
/// <see cref="TramiteCambioEstadoEmailComposer"/>: el sub-flujo de revocatoria no trajo assets de
/// marca propios y no hay razón de negocio para uno distinto — mismos avisos de trámite, mismo
/// remitente visual. Decisión documentada en el reporte de la HU #12579.
/// </para>
/// </summary>
public static class RevocationRequestEmailComposer
{
    private const string PrimaryBlue = "#2F6FED";
    private const string Ink = "#162244";
    private const string ApprovedGreen = "#2E7D32";
    private const string RejectedRed = "#C62828";
    private const string InfoAmber = "#8A6D00";
    private const string RentingInk = "#1A1A1A";
    private const string RentingMuted = "#4A4A4A";
    private const string RentingLink = "#2F5BEA";
    private const string PqrsUrl = "https://usadosrentingcolombia.com";
    private const string RentingSupportEmail = "servicio@rentingcolombia.com";
    private const string PrivacyPolicyUrl = "https://flitsas.com/politica-de-privacidad";
    private const string SupportEmail = "soporte@flitsas.com";

    public static (string Subject, string Html) ComposeFlit(
        string milestone, RevocationRequestEmailModel model, string assetsBaseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(milestone);
        ArgumentNullException.ThrowIfNull(model);
        var copy = ResolveCopy(milestone);
        var subject = BuildSubject(copy, model.Radicado);
        var html = BuildFlitHtml(copy, model, assetsBaseUrl);
        return (subject, html);
    }

    public static (string Subject, string Html) ComposeRenting(
        string milestone, RevocationRequestEmailModel model, string assetsBaseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(milestone);
        ArgumentNullException.ThrowIfNull(model);
        var copy = ResolveCopy(milestone);
        var subject = BuildSubject(copy, model.Radicado);
        var html = BuildRentingHtml(copy, model, assetsBaseUrl);
        return (subject, html);
    }

    private static string BuildSubject(MilestoneCopy copy, string radicado) =>
        $"[FLIT] {copy.SubjectPrefix} — {radicado}";

    private static string BuildFlitHtml(
        MilestoneCopy copy, RevocationRequestEmailModel model, string assetsBaseUrl)
    {
        var destinatario = Enc(model.DestinatarioNombre);
        var placa = Enc(model.Placa);
        var radicado = Enc(model.Radicado);
        var headerUrl = EncAttr(TramiteCambioEstadoEmailComposer.ResolveHeaderUrl(assetsBaseUrl));
        var logoUrl = EncAttr(TramiteCambioEstadoEmailComposer.ResolveLogoUrl(assetsBaseUrl));

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"es\"><head><meta charset=\"utf-8\"/></head>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<body style=\"margin:0;padding:0;background:#ffffff;font-family:Arial,Helvetica,sans-serif;color:{Ink};\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:640px;margin:0 auto;background:#ffffff;\">");
        sb.Append(CultureInfo.InvariantCulture,
            $"<tr><td style=\"padding:0;\"><img src=\"{headerUrl}\" alt=\"FLIT Version 2.0\" width=\"640\" style=\"display:block;width:100%;max-width:640px;height:auto;border:0;\"/></td></tr>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<tr><td style=\"padding:28px 28px 8px;text-align:center;\"><h1 style=\"margin:0;font-size:20px;letter-spacing:0.02em;color:{copy.AccentColor};\">{Enc(copy.Heading)}</h1></td></tr>");
        sb.Append("<tr><td style=\"padding:16px 28px 8px;font-size:14px;line-height:1.55;\">");
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 12px;\">Estimado/a Señor/a <strong style=\"color:{PrimaryBlue};\">{destinatario}</strong>.</p>");
        sb.Append(CultureInfo.InvariantCulture, $"<p style=\"margin:0 0 16px;\">{copy.IntroHtml}</p>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 6px;\"><strong style=\"color:{PrimaryBlue};\">Radicado:</strong> {radicado}</p>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 16px;\"><strong style=\"color:{PrimaryBlue};\">Placa del Vehículo:</strong> {placa}</p>");
        if (!string.IsNullOrWhiteSpace(model.Motivo) && copy.ShowMotivo)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"<p style=\"margin:0 0 16px;\"><strong style=\"color:{PrimaryBlue};\">Motivo:</strong> {EncMultiline(model.Motivo)}</p>");
        }
        sb.Append(CultureInfo.InvariantCulture, $"<p style=\"margin:0 0 20px;\">{copy.CierreHtml}</p>");
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
        MilestoneCopy copy, RevocationRequestEmailModel model, string assetsBaseUrl)
    {
        var destinatario = Enc(model.DestinatarioNombre);
        var placa = Enc(model.Placa);
        var radicado = Enc(model.Radicado);
        var headerUrl = EncAttr(TramiteCambioEstadoEmailComposer.ResolveRentingHeaderUrl(assetsBaseUrl));
        var footerUrl = EncAttr(TramiteCambioEstadoEmailComposer.ResolveRentingFooterUrl(assetsBaseUrl));
        var pqrsHref = EncAttr(PqrsUrl);
        var supportEmail = Enc(RentingSupportEmail);
        var supportMailto = EncAttr($"mailto:{RentingSupportEmail}");

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"es\"><head><meta charset=\"utf-8\"/>");
        sb.Append("<meta name=\"color-scheme\" content=\"light\"/>");
        sb.Append("<meta name=\"supported-color-schemes\" content=\"light\"/>");
        sb.Append("<style type=\"text/css\">:root{color-scheme:light;}</style></head>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<body style=\"margin:0;padding:0;background:#ffffff;font-family:Arial,Helvetica,sans-serif;color:{RentingInk};\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:640px;margin:0 auto;background:#ffffff;\">");
        sb.Append(CultureInfo.InvariantCulture,
            $"<tr><td bgcolor=\"#ffffff\" style=\"padding:0;background-color:#ffffff !important;\"><img src=\"{headerUrl}\" alt=\"Compra Tu Usado — Renting Colombia\" width=\"640\" style=\"display:block;width:100%;max-width:640px;height:auto;border:0;background-color:#ffffff;\"/></td></tr>");
        sb.Append("<tr><td style=\"padding:28px 32px 8px;font-size:15px;line-height:1.55;background-color:#ffffff;\">");
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 16px;\"><strong>{destinatario}</strong>, ¡Es un gusto saludarte!</p>");
        sb.Append(CultureInfo.InvariantCulture, $"<p style=\"margin:0 0 20px;\">{copy.IntroHtml}</p>");
        sb.Append("<p style=\"margin:0 0 10px;\"><strong>Detalles clave:</strong></p>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 6px;\"><strong>Radicado:</strong> {radicado}</p>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 18px;\"><strong>Placa del Vehículo:</strong> {placa}</p>");
        if (!string.IsNullOrWhiteSpace(model.Motivo) && copy.ShowMotivo)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"<p style=\"margin:0 0 18px;\"><strong>Motivo:</strong> {EncMultiline(model.Motivo)}</p>");
        }
        sb.Append(CultureInfo.InvariantCulture, $"<p style=\"margin:0 0 18px;\">{copy.CierreHtml}</p>");
        sb.Append("<p style=\"margin:0 0 8px;\">Si tienes alguna inquietud, comunícate con nosotros:</p>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 6px;\"><strong>Formularios web:</strong> <a href=\"{pqrsHref}\" style=\"color:{RentingLink};text-decoration:underline;\">Formulario PQRS (usadosrentingcolombia.com)</a></p>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 18px;\"><strong>Correo electrónico:</strong> <a href=\"{supportMailto}\" style=\"color:{RentingInk};text-decoration:none;\">{supportEmail}</a></p>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 8px;font-size:13px;line-height:1.5;color:{RentingMuted};\">Por favor, <strong>NO</strong> respondas a este mensaje, es un medio informativo automático.</p>");
        sb.Append("</td></tr>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<tr><td bgcolor=\"#ffffff\" style=\"padding:8px 24px 28px;background-color:#ffffff !important;\"><img src=\"{footerUrl}\" alt=\"Línea nacional y WhatsApp Renting Colombia\" width=\"592\" style=\"display:block;width:100%;max-width:592px;height:auto;border:0;margin:0 auto;background-color:#ffffff;\"/></td></tr>");
        sb.Append("</table></body></html>");
        return sb.ToString();
    }

    private sealed record MilestoneCopy(
        string SubjectPrefix, string Heading, string AccentColor, string IntroHtml, string CierreHtml, bool ShowMotivo);

    private static MilestoneCopy ResolveCopy(string milestone) => milestone switch
    {
        RevocationRequestEmailMilestone.Solicitada => new MilestoneCopy(
            SubjectPrefix: "Hemos recibido tu solicitud de revocatoria",
            Heading: "¡SOLICITUD DE REVOCATORIA RECIBIDA!",
            AccentColor: InfoAmber,
            IntroHtml: "Hemos recibido tu solicitud de revocatoria del trámite. El organismo de tránsito la revisará y te avisaremos por este medio en cuanto haya una decisión.",
            CierreHtml: "En FLIT, nos aseguramos de que los trámites de tránsito sean más ágiles, eficientes y sin contratiempos.",
            ShowMotivo: false),
        RevocationRequestEmailMilestone.Aprobada => new MilestoneCopy(
            SubjectPrefix: "Tu solicitud de revocatoria fue aprobada",
            Heading: "¡SOLICITUD DE REVOCATORIA APROBADA!",
            AccentColor: ApprovedGreen,
            IntroHtml: "El organismo de tránsito aprobó tu solicitud de revocatoria. El trámite quedó en estado <strong>Revocado</strong>.",
            CierreHtml: "En FLIT, nos aseguramos de que los trámites de tránsito sean más ágiles, eficientes y sin contratiempos.",
            ShowMotivo: false),
        RevocationRequestEmailMilestone.Rechazada => new MilestoneCopy(
            SubjectPrefix: "Tu solicitud de revocatoria fue rechazada",
            Heading: "SOLICITUD DE REVOCATORIA RECHAZADA",
            AccentColor: RejectedRed,
            IntroHtml: "El organismo de tránsito rechazó tu solicitud de revocatoria. El trámite permanece sin cambios en su estado actual.",
            CierreHtml: "Puedes radicar un nuevo intento de revocatoria cuando corrijas el motivo indicado abajo.",
            ShowMotivo: true),
        _ => throw new ArgumentOutOfRangeException(nameof(milestone), milestone, "Hito de revocatoria desconocido."),
    };

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
