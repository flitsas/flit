using System.Net;
using System.Text;
using Flit.Infrastructure.Notifications.Tramites;
using Flit.Modules.Security.Domain.Auth;

namespace Flit.Infrastructure.Notifications.Identity;

/// <summary>Tono de la caja de aviso opcional sobre el título del correo de verificación de identidad.</summary>
public enum IdentityEmailBannerTone
{
    /// <summary>Aviso informativo (verde suave): p. ej. «el enlace sigue siendo el tuyo».</summary>
    Info,

    /// <summary>Aviso de atención (ámbar suave): p. ej. una validación rechazada con enlace nuevo.</summary>
    Warning,
}

/// <summary>
/// Texto parametrizable de la estructura del correo de verificación de identidad (HU #13287, Épica #13202). Una variante
/// (enlace inicial hoy; «rechazo» con enlace nuevo en la rama C) solo cambia estos valores: la estructura HTML es única, en
/// <see cref="IdentityVerificationEmailLayout"/>. <see cref="IntroHtml"/> es HTML ya escapado por quien compone el contenido.
/// </summary>
public sealed record IdentityVerificationEmailContent(
    string Subject,
    string Eyebrow,
    string Title,
    string IntroHtml,
    IReadOnlyList<string> Steps,
    string ButtonLabel,
    string FallbackLinkIntro,
    string FooterNote,
    string? BannerText = null,
    IdentityEmailBannerTone BannerTone = IdentityEmailBannerTone.Info,
    string StepsHeading = "PASOS DE LA VERIFICACIÓN",
    string RequestedByLabel = "Solicitado por");

/// <summary>
/// Estructura única del correo de verificación de identidad: tarjeta blanca centrada (~520 px) sobre fondo crema, HTML de correo
/// con tablas y estilos en línea (sin fuentes ni hojas externas, <c>alt</c> en toda imagen). Logo y producto salen del tema de
/// correo de la red del tenant (<see cref="EmailTheme"/>: FLIT o marca blanca). El nombre del titular, el producto y todo valor del
/// tema se escapan; el color del tema se valida como <c>#RRGGBB</c>. Nunca incluye datos sensibles: solo el enlace personal.
/// </summary>
public static class IdentityVerificationEmailLayout
{
    private const string PageBackground = "#F8F8F4";
    private const string CardBorder = "#E3E6EA";
    private const string Ink = "#162244";
    private const string Muted = "#59677D";
    private const string StepBubble = "#E8EDFB";
    private const string FlitBlue = "#557EFF";

    /// <summary>Bug #13449 — canal de soporte de FLIT que el cliente ve al recibir la verificación.</summary>
    public const string SupportEmail = "soporte@flitsas.com";

    public static string Render(
        EmailTheme theme, string assetsBaseUrl, string? recipientName, string link, IdentityVerificationEmailContent content)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentException.ThrowIfNullOrWhiteSpace(link);
        ArgumentNullException.ThrowIfNull(content);

        var product = Enc(ProductName(theme));
        var accent = theme.IsBrand ? BrandedEmailChrome.LinkColor(theme) : FlitBlue;
        var logoUrl = LogoUrl(theme, assetsBaseUrl);
        var href = Enc(link);
        var greeting = string.IsNullOrWhiteSpace(recipientName) ? "Hola." : $"Hola {Enc(recipientName.Trim())}.";

        var sb = new StringBuilder(6144);
        sb.Append("<!DOCTYPE html><html lang=\"es\"><head><meta charset=\"utf-8\"/>");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"/>");
        sb.Append("<meta name=\"color-scheme\" content=\"light\"/><meta name=\"supported-color-schemes\" content=\"light\"/>");
        sb.Append("<title>").Append(Enc(content.Title)).Append("</title></head>");
        sb.Append("<body style=\"margin:0;padding:0;background:").Append(PageBackground)
            .Append(";font-family:Arial,Helvetica,sans-serif;color:").Append(Ink).Append(";\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"background:")
            .Append(PageBackground).Append(";\"><tr><td align=\"center\" style=\"padding:24px 12px;\">");
        sb.Append("<table role=\"presentation\" width=\"520\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"width:100%;max-width:520px;\">");

        // Cabecera: logo + etiqueta.
        sb.Append("<tr><td style=\"padding:0 4px 6px;\">").Append(LogoHtml(logoUrl, product, 34, accent)).Append("</td></tr>");
        sb.Append("<tr><td style=\"padding:0 4px 14px;font-size:11px;font-weight:700;letter-spacing:0.12em;color:").Append(accent)
            .Append(";text-transform:uppercase;\">").Append(Enc(content.Eyebrow)).Append("</td></tr>");

        // Tarjeta.
        sb.Append("<tr><td style=\"background:#ffffff;border:1px solid ").Append(CardBorder)
            .Append(";border-radius:16px;padding:26px;\">");

        if (!string.IsNullOrWhiteSpace(content.BannerText))
        {
            var (bannerBg, bannerInk) = content.BannerTone == IdentityEmailBannerTone.Warning
                ? ("#FFF4E0", "#7A4B00")
                : ("#E3F2EC", "#164A3A");
            sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"margin:0 0 20px;\"><tr>")
                .Append("<td style=\"background:").Append(bannerBg).Append(";color:").Append(bannerInk)
                .Append(";border-radius:10px;padding:12px 14px;font-size:13px;line-height:1.45;\">")
                .Append(Enc(content.BannerText)).Append("</td></tr></table>");
        }

        sb.Append("<h1 style=\"margin:0 0 14px;font-size:22px;line-height:1.25;color:").Append(Ink).Append(";\">")
            .Append(Enc(content.Title)).Append("</h1>");
        sb.Append("<p style=\"margin:0 0 12px;font-size:15px;line-height:1.5;color:").Append(Ink).Append(";\">")
            .Append(greeting).Append("</p>");
        sb.Append("<p style=\"margin:0 0 18px;font-size:15px;line-height:1.6;color:").Append(Muted).Append(";\">")
            .Append(content.IntroHtml).Append("</p>");

        // Solicitado por.
        sb.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"margin:0 0 22px;\"><tr>")
            .Append("<td style=\"padding:0 12px 0 0;vertical-align:middle;\">").Append(LogoHtml(logoUrl, product, 26, accent)).Append("</td>")
            .Append("<td style=\"vertical-align:middle;font-size:13px;line-height:1.35;color:").Append(Muted).Append(";\">")
            .Append(Enc(content.RequestedByLabel)).Append("<br/><strong style=\"font-size:14px;color:").Append(Ink).Append(";\">")
            .Append(product).Append("</strong></td></tr></table>");

        // Pasos.
        sb.Append("<p style=\"margin:0 0 10px;font-size:12px;font-weight:700;letter-spacing:0.06em;color:").Append(Muted)
            .Append(";\">").Append(Enc(content.StepsHeading)).Append("</p>");
        sb.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"margin:0 0 24px;\">");
        for (var i = 0; i < content.Steps.Count; i++)
        {
            sb.Append("<tr><td style=\"padding:0 12px 10px 0;vertical-align:middle;\">")
                .Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\"><tr>")
                .Append("<td width=\"26\" height=\"26\" align=\"center\" style=\"width:26px;height:26px;background:").Append(StepBubble)
                .Append(";border-radius:13px;font-size:12px;font-weight:700;color:").Append(accent).Append(";\">")
                .Append(i + 1).Append("</td></tr></table></td>")
                .Append("<td style=\"padding:0 0 10px;vertical-align:middle;font-size:14px;color:").Append(Ink).Append(";\">")
                .Append(Enc(content.Steps[i])).Append("</td></tr>");
        }
        sb.Append("</table>");

        // Botón (ancho según contenido) y enlace de respaldo.
        sb.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"margin:0 0 18px;\"><tr>")
            .Append("<td style=\"background:").Append(accent).Append(";border-radius:10px;\">")
            .Append("<a href=\"").Append(href).Append("\" style=\"display:inline-block;padding:14px 28px;color:#ffffff;")
            .Append("text-decoration:none;font-size:15px;font-weight:700;border-radius:10px;\">")
            .Append(Enc(content.ButtonLabel)).Append("</a></td></tr></table>");
        sb.Append("<p style=\"margin:0 0 6px;font-size:12px;line-height:1.5;color:").Append(Muted).Append(";\">")
            .Append(Enc(content.FallbackLinkIntro)).Append("<br/><a href=\"").Append(href).Append("\" style=\"color:").Append(accent)
            .Append(";word-break:break-all;\">").Append(href).Append("</a></p>");
        sb.Append("<p style=\"margin:10px 0 0;font-size:12px;line-height:1.5;color:").Append(Muted).Append(";\">")
            .Append(Enc(content.FooterNote)).Append("</p>");
        sb.Append("<p style=\"margin:10px 0 0;font-size:12px;line-height:1.5;color:").Append(Muted).Append(";\">")
            .Append("¿Necesitas ayuda? Escríbenos a <a href=\"mailto:").Append(SupportEmail).Append("\" style=\"color:").Append(accent)
            .Append(";\">").Append(SupportEmail).Append("</a>.</p>");

        sb.Append("</td></tr></table></td></tr></table></body></html>");
        return sb.ToString();
    }

    /// <summary>Nombre del producto para asunto y cuerpo: el de la marca de la red, o FLIT 2.0.</summary>
    public static string ProductName(EmailTheme theme) =>
        new(Clean(string.IsNullOrWhiteSpace(theme.PlatformName) ? EmailTheme.Flit.PlatformName : theme.PlatformName).ToArray());

    private static IEnumerable<char> Clean(string value) => value.Trim().Select(c => char.IsControl(c) ? ' ' : c);

    private static string? LogoUrl(EmailTheme theme, string assetsBaseUrl)
    {
        if (theme.IsBrand)
        {
            return !string.IsNullOrWhiteSpace(theme.LogoUrl)
                && Uri.TryCreate(theme.LogoUrl, UriKind.Absolute, out var u)
                && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)
                ? theme.LogoUrl
                : null;
        }

        return TramiteCambioEstadoEmailComposer.ResolveLogoUrl(assetsBaseUrl);
    }

    private static string LogoHtml(string? logoUrl, string productEncoded, int heightPx, string accent) =>
        logoUrl is null
            ? $"<span style=\"font-size:20px;font-weight:700;color:{accent};\">{productEncoded}</span>"
            : $"<img src=\"{EncAttr(logoUrl)}\" alt=\"{productEncoded}\" height=\"{heightPx}\" style=\"display:block;border:0;height:{heightPx}px;width:auto;\"/>";

    private static string Enc(string value) => WebUtility.HtmlEncode(value);

    private static string EncAttr(string value) => WebUtility.HtmlEncode(value).Replace("\"", "&quot;", StringComparison.Ordinal);
}
