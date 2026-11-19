using System.Globalization;
using System.Net;
using System.Text;
using Flit.Modules.Security.Domain.Auth;
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
/// <para>
/// HU #12428 (Feature #12405, ADR-0060) — la variante FLIT admite un <see cref="EmailTheme"/>
/// resuelto por red: con <see cref="EmailThemeKind.Brand"/> el mismo cuerpo funcional se envuelve
/// en <see cref="BrandedEmailChrome"/> (misma convención que <see cref="AsignacionPlacaEmailComposer"/>
/// y <see cref="TramiteCambioEstadoEmailComposer"/>); con <c>Flit</c>/<c>null</c> el HTML anterior se
/// conserva byte a byte (AC9). El canal Renting nunca recibe tema (AC8).
/// </para>
/// </summary>
public static class RevocationRequestEmailComposer
{
    private const string RentingInk = "#1A1A1A";
    private const string RentingMuted = "#4A4A4A";
    private const string RentingLink = "#2F5BEA";
    private const string PqrsUrl = "https://usadosrentingcolombia.com";
    private const string RentingSupportEmail = "servicio@rentingcolombia.com";

    /// <param name="theme">HU #12428 AC1/AC2/AC9 — igual convención que
    /// <c>AsignacionPlacaEmailComposer.ComposeFlit</c>: aditivo, <c>Flit</c>/<c>null</c> preserva
    /// el HTML anterior byte a byte.</param>
    public static (string Subject, string Html) ComposeFlit(
        string milestone, RevocationRequestEmailModel model, string assetsBaseUrl, EmailTheme? theme = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(milestone);
        ArgumentNullException.ThrowIfNull(model);
        var copy = ResolveCopy(milestone);
        var subject = BuildSubject(copy, model.Radicado);
        var html = theme is { IsBrand: true }
            ? BuildBrandedHtml(copy, model, theme)
            : BuildFlitHtml(copy, model, assetsBaseUrl);
        return (subject, html);
    }

    /// <summary>HU #12428 AC2/AC3/AC6 — mismo dato funcional que <see cref="BuildFlitHtml"/> (saludo,
    /// intro del hito, radicado, placa, motivo si aplica, cierre) sobre el chrome de
    /// <see cref="BrandedEmailChrome"/>. El cierre usa la variante neutra de marca (sin "En FLIT")
    /// — mismo criterio que <c>TramiteCambioEstadoEmailComposer.BuildBrandedHtml</c>.</summary>
    private static string BuildBrandedHtml(
        MilestoneCopy copy, RevocationRequestEmailModel model, EmailTheme theme)
    {
        var destinatario = Enc(model.DestinatarioNombre);
        var placa = Enc(model.Placa);
        var radicado = Enc(model.Radicado);
        var linkColor = BrandedEmailChrome.LinkColor(theme);

        var body = new StringBuilder();
        body.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 12px;\">Estimado/a Señor/a <strong style=\"color:{linkColor};\">{destinatario}</strong>.</p>");
        body.Append(CultureInfo.InvariantCulture, $"<p style=\"margin:0 0 16px;\">{copy.IntroHtml}</p>");
        body.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 6px;\"><strong style=\"color:{linkColor};\">Radicado:</strong> {radicado}</p>");
        body.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0 0 16px;\"><strong style=\"color:{linkColor};\">Placa del Vehículo:</strong> {placa}</p>");
        if (!string.IsNullOrWhiteSpace(model.Motivo) && copy.ShowMotivo)
        {
            body.Append(CultureInfo.InvariantCulture,
                $"<p style=\"margin:0 0 16px;\"><strong style=\"color:{linkColor};\">Motivo:</strong> {EncMultiline(model.Motivo)}</p>");
        }
        body.Append(CultureInfo.InvariantCulture, $"<p style=\"margin:0;\">{copy.CierreBrandHtml}</p>");

        return BrandedEmailChrome.Wrap(theme, copy.Heading, body.ToString());
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

    /// <summary>
    /// Correos 2.0 — variante FLIT sobre <see cref="FlitEmailLayout"/>: título en el azul único del
    /// layout (el hito se lee en el propio título); radicado, placa y motivo en la tarjeta.
    /// </summary>
    private static string BuildFlitHtml(
        MilestoneCopy copy, RevocationRequestEmailModel model, string assetsBaseUrl)
    {
        var filas = new List<(string, string)>
        {
            ("Radicado:", Enc(model.Radicado)),
            ("Placa del Vehículo:", Enc(model.Placa)),
        };
        if (!string.IsNullOrWhiteSpace(model.Motivo) && copy.ShowMotivo)
            filas.Add(("Motivo:", EncMultiline(model.Motivo)));

        var cuerpo = new StringBuilder();
        cuerpo.Append(FlitEmailLayout.Parrafo(
            $"Estimado/a Señor/a {FlitEmailLayout.Destacado(Enc(model.DestinatarioNombre))}."));
        cuerpo.Append(FlitEmailLayout.Parrafo(copy.IntroHtml, margen: "0 0 20px"));
        cuerpo.Append(FlitEmailLayout.TarjetaDetalle(null, filas));
        cuerpo.Append(FlitEmailLayout.Parrafo(copy.CierreHtml, centrado: false));
        cuerpo.Append(FlitEmailLayout.Parrafo("Cordialmente,", centrado: false, margen: "0"));

        return FlitEmailLayout.Documento(
            assetsBaseUrl,
            FlitEmailLayout.Titulo(copy.Heading),
            cuerpo.ToString(),
            FlitEmailLayout.PieFlit(assetsBaseUrl, FlitEmailLayout.SoporteEstandarHtml, politicaPrivacidad: true));
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

    /// <param name="CierreBrandHtml">Cierre para el chrome de marca (HU #12428): sin la mención
    /// literal a FLIT, porque el remitente visual es la marca de la red.</param>
    private sealed record MilestoneCopy(
        string SubjectPrefix, string Heading, string IntroHtml, string CierreHtml, bool ShowMotivo,
        string CierreBrandHtml);

    private const string CierreBrandGenerico =
        "Nos aseguramos de que los trámites de tránsito sean más ágiles, eficientes y sin contratiempos.";

    private static MilestoneCopy ResolveCopy(string milestone) => milestone switch
    {
        RevocationRequestEmailMilestone.Solicitada => new MilestoneCopy(
            SubjectPrefix: "Hemos recibido tu solicitud de revocatoria",
            Heading: "¡SOLICITUD DE REVOCATORIA RECIBIDA!",
            IntroHtml: "Hemos recibido tu solicitud de revocatoria del trámite. El organismo de tránsito la revisará y te avisaremos por este medio en cuanto haya una decisión.",
            CierreHtml: "En FLIT, nos aseguramos de que los trámites de tránsito sean más ágiles, eficientes y sin contratiempos.",
            ShowMotivo: false,
            CierreBrandHtml: CierreBrandGenerico),
        RevocationRequestEmailMilestone.Aprobada => new MilestoneCopy(
            SubjectPrefix: "Tu solicitud de revocatoria fue aprobada",
            Heading: "¡SOLICITUD DE REVOCATORIA APROBADA!",
            IntroHtml: "El organismo de tránsito aprobó tu solicitud de revocatoria. El trámite quedó en estado <strong>Revocado</strong>.",
            CierreHtml: "En FLIT, nos aseguramos de que los trámites de tránsito sean más ágiles, eficientes y sin contratiempos.",
            ShowMotivo: false,
            CierreBrandHtml: CierreBrandGenerico),
        RevocationRequestEmailMilestone.Rechazada => new MilestoneCopy(
            SubjectPrefix: "Tu solicitud de revocatoria fue rechazada",
            Heading: "SOLICITUD DE REVOCATORIA RECHAZADA",
            IntroHtml: "El organismo de tránsito rechazó tu solicitud de revocatoria. El trámite permanece sin cambios en su estado actual.",
            CierreHtml: "Puedes radicar un nuevo intento de revocatoria cuando corrijas el motivo indicado abajo.",
            ShowMotivo: true,
            CierreBrandHtml: "Puedes radicar un nuevo intento de revocatoria cuando corrijas el motivo indicado arriba."),
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
