using System.Globalization;
using System.Net;
using System.Text;

namespace Flit.Modules.Security.Application.Auth;

/// <summary>
/// Shell visual compartido de correos FLIT (módulo Security): banner, título destacado,
/// cuerpo centrado, logo y pie con soporte + política de privacidad.
/// </summary>
/// <remarks>
/// Los composers de cada plantilla aportan el título y el HTML interno (enunciados y enlaces);
/// este layout solo aplica la marca. Assets públicos vía <see cref="DefaultAssetsBaseUrl"/>.
/// </remarks>
public static class FlitBrandedEmailLayout
{
    public const string DefaultAssetsBaseUrl = "https://dev.flitsas.online/email-assets";
    public const string DefaultHeaderFileName = "tramite-cambio-estado-header.png";
    public const string DefaultLogoFileName = "flit-logo.png";
    public const string PrivacyPolicyUrl = "https://flitsas.com/politica-de-privacidad";
    public const string SupportEmail = "soporte@flitsas.com";

    private const string PrimaryBlue = "#6080F0";
    private const string TitleHighlight = "#E8EEFF";
    private const string Ink = "#4A5568";
    private const string LinkBlue = "#2F6FED";

    public static string ResolveHeaderUrl(string assetsBaseUrl) =>
        CombineAssetUrl(assetsBaseUrl, DefaultHeaderFileName);

    public static string ResolveLogoUrl(string assetsBaseUrl) =>
        CombineAssetUrl(assetsBaseUrl, DefaultLogoFileName);

    /// <summary>
    /// Envuelve el cuerpo de una plantilla Security en el formato FLIT de marca.
    /// </summary>
    /// <param name="headline">Título principal (ya sin HTML).</param>
    /// <param name="bodyInnerHtml">Fragmentos HTML del cuerpo (párrafos/enlaces ya escapados).</param>
    /// <param name="closingHeadline">Título de cierre opcional (sin HTML).</param>
    /// <param name="assetsBaseUrl">Base HTTPS de assets; si es null/vacío usa el default público.</param>
    public static string Wrap(
        string headline,
        string bodyInnerHtml,
        string? closingHeadline = null,
        string? assetsBaseUrl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(headline);
        ArgumentNullException.ThrowIfNull(bodyInnerHtml);

        var baseUrl = string.IsNullOrWhiteSpace(assetsBaseUrl)
            ? DefaultAssetsBaseUrl
            : assetsBaseUrl.Trim().TrimEnd('/');
        var headerUrl = EncAttr(ResolveHeaderUrl(baseUrl));
        var logoUrl = EncAttr(ResolveLogoUrl(baseUrl));
        var headlineEnc = Enc(headline);
        var closingEnc = string.IsNullOrWhiteSpace(closingHeadline) ? null : Enc(closingHeadline);

        var sb = new StringBuilder(2048);
        sb.Append("<!DOCTYPE html><html lang=\"es\"><head><meta charset=\"utf-8\"/>");
        sb.Append("<meta name=\"color-scheme\" content=\"light dark\"/>");
        sb.Append("<meta name=\"supported-color-schemes\" content=\"light dark\"/>");
        sb.Append("</head>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<body style=\"margin:0;padding:0;background:#ffffff;font-family:Arial,Helvetica,sans-serif;color:{Ink};\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:640px;margin:0 auto;background:#ffffff;\">");

        sb.Append(CultureInfo.InvariantCulture,
            $"<tr><td style=\"padding:0;\"><img src=\"{headerUrl}\" alt=\"FLIT Versión 2.0\" width=\"640\" style=\"display:block;width:100%;max-width:640px;height:auto;border:0;\"/></td></tr>");

        sb.Append("<tr><td style=\"padding:28px 28px 8px;text-align:center;\">");
        sb.Append(CultureInfo.InvariantCulture,
            $"<h1 style=\"margin:0 auto;display:inline-block;padding:8px 16px;font-size:22px;letter-spacing:0.02em;color:{PrimaryBlue};background-color:{TitleHighlight};font-weight:700;\">{headlineEnc}</h1>");
        sb.Append("</td></tr>");

        sb.Append(CultureInfo.InvariantCulture,
            $"<tr><td style=\"padding:16px 36px 8px;text-align:center;font-size:15px;line-height:1.6;color:{Ink};\">{bodyInnerHtml}</td></tr>");

        if (closingEnc is not null)
        {
            sb.Append("<tr><td style=\"padding:12px 28px 8px;text-align:center;\">");
            sb.Append(CultureInfo.InvariantCulture,
                $"<p style=\"margin:0;font-size:18px;font-weight:700;color:{PrimaryBlue};\">{closingEnc}</p>");
            sb.Append("</td></tr>");
        }

        sb.Append("<tr><td style=\"padding:16px 28px 24px;text-align:center;\">");
        sb.Append(CultureInfo.InvariantCulture,
            $"<img src=\"{logoUrl}\" alt=\"flit\" width=\"120\" style=\"display:inline-block;border:0;\"/>");
        sb.Append("</td></tr>");

        sb.Append("<tr><td style=\"padding:0 28px 28px;\">");
        sb.Append("<hr style=\"border:none;border-top:1px solid #E5EAF2;margin:0 0 16px;\"/>");
        sb.Append("<p style=\"margin:0 0 12px;font-size:12px;line-height:1.5;color:#6B778C;text-align:center;\">");
        sb.Append("Si tienes alguna pregunta o necesitas ayuda, no dudes en ponerte en contacto con nosotros. ");
        sb.Append(CultureInfo.InvariantCulture,
            $"Puedes hacerlo a través de <strong>{Enc(SupportEmail)}</strong> o nuestro <strong>chat online</strong>.");
        sb.Append("</p>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<p style=\"margin:0;text-align:center;\"><a href=\"{EncAttr(PrivacyPolicyUrl)}\" style=\"color:{LinkBlue};font-size:12px;text-decoration:underline;\">POLÍTICA DE PRIVACIDAD</a></p>");
        sb.Append("</td></tr></table></body></html>");
        return sb.ToString();
    }

    public static string Paragraph(string text) =>
        $"<p style=\"margin:0 0 12px;\">{Enc(text)}</p>";

    public static string ParagraphHtml(string innerHtml) =>
        $"<p style=\"margin:0 0 12px;\">{innerHtml}</p>";

    public static string ActionLink(string href, string label) =>
        $"<p style=\"margin:0 0 16px;\"><a href=\"{EncAttr(href)}\" style=\"color:{LinkBlue};font-size:15px;text-decoration:underline;word-break:break-all;\">{Enc(label)}</a></p>";

    private static string CombineAssetUrl(string assetsBaseUrl, string fileName)
    {
        var baseUrl = string.IsNullOrWhiteSpace(assetsBaseUrl)
            ? DefaultAssetsBaseUrl
            : assetsBaseUrl.Trim().TrimEnd('/');
        return $"{baseUrl}/{fileName}";
    }

    private static string Enc(string value) => WebUtility.HtmlEncode(value);

    private static string EncAttr(string value) => WebUtility.HtmlEncode(value);
}
