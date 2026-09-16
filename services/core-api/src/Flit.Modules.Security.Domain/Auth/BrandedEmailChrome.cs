using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Flit.Modules.Security.Domain.Auth;

/// <summary>
/// Estructura fija de encabezado/pie sobre un <see cref="EmailTheme"/> (HU #12428 AC2/AC3/AC6/AC7):
/// tabla de 600 px, estilos EN LÍNEA, sin fuentes externas, <c>alt</c> en toda imagen, sin
/// <c>&lt;link&gt;</c>/<c>&lt;style&gt;</c> externos. Único punto de interpolación de
/// <see cref="EmailTheme"/> en HTML: todos los valores pasan por <see cref="WebUtility.HtmlEncode"/>
/// y los colores se validan contra <c>#RRGGBB</c> (AC6 — "ningún campo admite marcado ni texto
/// libre"); un valor que no cumpla el formato cae al equivalente de <see cref="EmailTheme.Flit"/>,
/// nunca se interpola crudo.
/// </summary>
public static class BrandedEmailChrome
{
    private const int MaxWidthPx = 600;
    private static readonly Regex HexColorPattern = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    /// <summary>
    /// Envuelve <paramref name="bodyInnerHtml"/> (ya construido y escapado por el composer llamador)
    /// en la estructura única: encabezado (logo + nombre), título, cuerpo, título de cierre opcional,
    /// pie (nombre de la marca). Con <see cref="EmailTheme.Kind"/> <c>Flit</c> el llamador NO debe
    /// invocar este método (AC9 exige el camino byte a byte anterior) — este wrapper es
    /// exclusivamente para <c>Brand</c>.
    /// </summary>
    public static string Wrap(EmailTheme theme, string headline, string bodyInnerHtml, string? closingHeadline = null)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentException.ThrowIfNullOrWhiteSpace(headline);
        ArgumentNullException.ThrowIfNull(bodyInnerHtml);

        var primary = SafeColor(theme.Primary, EmailTheme.Flit.Primary);

        var sb = new StringBuilder(2048);
        sb.Append("<!DOCTYPE html><html lang=\"es\"><head><meta charset=\"utf-8\"/>");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"/>");
        sb.Append("<meta name=\"color-scheme\" content=\"light\"/>");
        sb.Append("<meta name=\"supported-color-schemes\" content=\"light\"/>");
        sb.Append("</head>");
        sb.Append("<body style=\"margin:0;padding:0;background:#ffffff;font-family:Arial,Helvetica,sans-serif;\">");
        sb.Append(CultureInvariant($"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"max-width:{MaxWidthPx}px;margin:0 auto;background:#ffffff;\">"));

        sb.Append(HeaderRowHtml(theme));

        sb.Append(CultureInvariant($"<tr><td style=\"padding:24px 28px 8px;text-align:center;\"><h1 style=\"margin:0;font-size:20px;letter-spacing:0.02em;color:{primary};\">{Enc(headline)}</h1></td></tr>"));
        sb.Append(CultureInvariant($"<tr><td style=\"padding:16px 28px 8px;font-size:14px;line-height:1.55;color:#162244;\">{bodyInnerHtml}</td></tr>"));

        if (!string.IsNullOrWhiteSpace(closingHeadline))
        {
            sb.Append(CultureInvariant($"<tr><td style=\"padding:8px 28px 8px;text-align:center;\"><p style=\"margin:0;font-size:18px;font-weight:700;color:{primary};\">{Enc(closingHeadline)}</p></td></tr>"));
        }

        sb.Append(FooterRowHtml(theme));
        sb.Append("</table></body></html>");
        return sb.ToString();
    }

    /// <summary>
    /// Fila de encabezado: logotipo por URL absoluta (AC3, <c>alt</c> con el nombre de la marca) si
    /// <see cref="EmailTheme.LogoUrl"/> viene informado; si no (marca publicada sin logo, borde), el
    /// nombre en texto sobre una banda del color principal — nunca se deja el encabezado vacío.
    /// </summary>
    public static string HeaderRowHtml(EmailTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var primary = SafeColor(theme.Primary, EmailTheme.Flit.Primary);
        var onPrimary = SafeColor(theme.OnPrimary, EmailTheme.Flit.OnPrimary);
        var platformName = Enc(string.IsNullOrWhiteSpace(theme.PlatformName) ? EmailTheme.Flit.PlatformName : theme.PlatformName);

        if (!string.IsNullOrWhiteSpace(theme.LogoUrl) && IsAbsoluteHttpUrl(theme.LogoUrl))
        {
            var logoUrl = EncAttr(theme.LogoUrl);
            return CultureInvariant(
                $"<tr><td style=\"padding:20px 28px;text-align:center;background-color:{primary};\">" +
                $"<img src=\"{logoUrl}\" alt=\"{platformName}\" height=\"40\" style=\"display:inline-block;border:0;max-height:40px;width:auto;\"/>" +
                "</td></tr>");
        }

        return CultureInvariant(
            $"<tr><td style=\"padding:20px 28px;text-align:center;background-color:{primary};\">" +
            $"<span style=\"color:{onPrimary};font-size:18px;font-weight:700;letter-spacing:0.02em;\">{platformName}</span>" +
            "</td></tr>");
    }

    /// <summary>Pie con el nombre de la marca (AC2, última cláusula).</summary>
    public static string FooterRowHtml(EmailTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var platformName = Enc(string.IsNullOrWhiteSpace(theme.PlatformName) ? EmailTheme.Flit.PlatformName : theme.PlatformName);

        return CultureInvariant(
            "<tr><td style=\"padding:16px 28px 24px;\">" +
            "<hr style=\"border:none;border-top:1px solid #E5EAF2;margin:0 0 16px;\"/>" +
            $"<p style=\"margin:0;font-size:12px;line-height:1.5;color:#6B778C;text-align:center;\">Mensaje automático de <strong>{platformName}</strong> — no respondas a este correo.</p>" +
            "</td></tr>");
    }

    /// <summary>AC2 — color aplicable a botones/enlaces del cuerpo (ya validado). Uso por composers
    /// que necesitan el mismo color del chrome dentro de su propio cuerpo funcional.</summary>
    public static string LinkColor(EmailTheme theme) => SafeColor(theme.Primary, EmailTheme.Flit.Primary);

    /// <summary>AC6 — nunca deja pasar un valor que no sea <c>#RRGGBB</c>.</summary>
    private static string SafeColor(string? hex, string fallback) =>
        !string.IsNullOrWhiteSpace(hex) && HexColorPattern.IsMatch(hex.Trim()) ? hex.Trim() : fallback;

    private static bool IsAbsoluteHttpUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed)
        && (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp);

    private static string Enc(string value) => WebUtility.HtmlEncode(value);

    private static string EncAttr(string value) => WebUtility.HtmlEncode(value).Replace("\"", "&quot;", StringComparison.Ordinal);

    private static string CultureInvariant(string value) => value;
}
