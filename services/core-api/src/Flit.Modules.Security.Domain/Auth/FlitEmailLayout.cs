using System.Net;
using System.Text;

namespace Flit.Modules.Security.Domain.Auth;

/// <summary>
/// Layout ÚNICO de los correos con tema FLIT («Correos 2.0»): banner, título azul en mayúsculas,
/// saludo, tarjeta de detalle, botón y pie con logo, divisor, soporte y política de privacidad.
/// Vive junto a <see cref="BrandedEmailChrome"/> para que lo usen tanto Security.Application como
/// Infrastructure; la marca blanca y Renting NO pasan por aquí.
/// </summary>
/// <remarks>
/// <para><b>HTML de correo:</b> tablas y estilos en línea, sin <c>&lt;style&gt;</c> ni fuentes externas
/// (Gmail los descarta), <c>alt</c> en toda imagen, ancho fluido con tope de 600 px y una tabla
/// fantasma de 600 px solo para Outlook de escritorio, que ignora <c>max-width</c>.</para>
/// <para><b>Copy:</b> el layout no escribe texto propio. Todo lo visible lo aporta el composer, de
/// modo que el rediseño cambie el marcado y nunca las palabras (guardia de copy en
/// <c>FlitEmailCopyGoldenTests</c>). Por eso el pie recibe su texto de soporte: cada correo
/// conserva el suyo.</para>
/// <para><b>Escape:</b> los parámetros terminados en <c>Html</c> se insertan tal cual (los escapa quien
/// compone); el resto pasa por <see cref="WebUtility.HtmlEncode"/>.</para>
/// </remarks>
public static class FlitEmailLayout
{
    /// <summary>
    /// Bug #13194 — respaldo SOLO para desarrollo local (frontend en <c>localhost:3000</c>). Los
    /// ambientes desplegados definen <c>Notifications__EmailAssets__BaseUrl</c>; nunca se cae a DEV.
    /// </summary>
    public const string DefaultAssetsBaseUrl = "http://localhost:3000/email-assets";

    /// <summary>Banner del material «Correos 2.0» (960×194, se muestra a 600 px).</summary>
    public const string BannerFileName = "flit-banner-correo.png";

    /// <summary>Logo de firma del pie (169×52).</summary>
    public const string LogoFileName = "flit-logo-firma.png";

    public const string SupportEmail = BrandedEmailChrome.FlitSupportEmail;
    public const string PrivacyPolicyUrl = BrandedEmailChrome.FlitPrivacyPolicyUrl;

    // Tokens del UI Kit 2026 (packages/ui). Contraste sobre blanco: Ink 15:1, Muted 5.9:1,
    // Accent 5.3:1, Title 3.9:1 (solo texto grande y en negrita).
    public const string Ink = "#162744";
    public const string Muted = "#59677D";
    public const string Accent = "#1D53FF";
    public const string Title = "#557EFF";
    public const string CardBorder = "#E3E9F5";
    public const string CardRowBorder = "#EEF2F8";
    public const string ButtonFrom = "#007D79";
    private const string FontStack = "'Poppins',Arial,Helvetica,sans-serif";
    private const int MaxWidthPx = 600;

    /// <summary>URL absoluta de un asset de correo; base vacía ⇒ <see cref="DefaultAssetsBaseUrl"/>.</summary>
    public static string AssetUrl(string? assetsBaseUrl, string fileName)
    {
        var baseUrl = string.IsNullOrWhiteSpace(assetsBaseUrl)
            ? DefaultAssetsBaseUrl
            : assetsBaseUrl.Trim().TrimEnd('/');
        return $"{baseUrl}/{fileName}";
    }

    public static string BannerUrl(string? assetsBaseUrl) => AssetUrl(assetsBaseUrl, BannerFileName);

    public static string LogoUrl(string? assetsBaseUrl) => AssetUrl(assetsBaseUrl, LogoFileName);

    /// <summary>
    /// Documento completo: banner, <paramref name="tituloHtml"/> (de <see cref="Titulo"/>, opcional),
    /// cuerpo y <paramref name="pieHtml"/> (de <see cref="PieFlit"/>).
    /// </summary>
    /// <param name="documentTitle">Texto del <c>&lt;title&gt;</c> (no visible en el cuerpo).</param>
    public static string Documento(
        string? assetsBaseUrl, string? tituloHtml, string cuerpoHtml, string pieHtml, string? documentTitle = null)
    {
        ArgumentNullException.ThrowIfNull(cuerpoHtml);
        ArgumentNullException.ThrowIfNull(pieHtml);

        var sb = new StringBuilder(4096);
        sb.Append("<!DOCTYPE html><html lang=\"es\"><head><meta charset=\"utf-8\"/>");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"/>");
        sb.Append("<meta name=\"color-scheme\" content=\"light\"/><meta name=\"supported-color-schemes\" content=\"light\"/>");
        if (!string.IsNullOrWhiteSpace(documentTitle))
            sb.Append("<title>").Append(Enc(documentTitle)).Append("</title>");
        sb.Append("</head>");
        sb.Append("<body style=\"margin:0;padding:0;background:#ffffff;\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" bgcolor=\"#ffffff\" style=\"background:#ffffff;\"><tr><td align=\"center\" style=\"padding:0;\">");
        sb.Append("<!--[if mso]><table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\"><tr><td><![endif]-->");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"width:100%;max-width:")
            .Append(MaxWidthPx).Append("px;margin:0 auto;background:#ffffff;font-family:").Append(FontStack)
            .Append(";color:").Append(Ink).Append(";\">");

        sb.Append("<tr><td style=\"padding:0;\"><img src=\"").Append(EncAttr(BannerUrl(assetsBaseUrl)))
            .Append("\" alt=\"FLIT Versión 2.0\" width=\"600\" style=\"display:block;width:100%;max-width:600px;height:auto;border:0;\"/></td></tr>");

        if (!string.IsNullOrEmpty(tituloHtml))
            sb.Append(tituloHtml);

        sb.Append("<tr><td style=\"padding:8px 24px 8px;font-size:15px;line-height:1.6;color:").Append(Ink).Append(";\">")
            .Append(cuerpoHtml).Append("</td></tr>");
        sb.Append(pieHtml);
        sb.Append("</table>");
        sb.Append("<!--[if mso]></td></tr></table><![endif]-->");
        sb.Append("</td></tr></table></body></html>");
        return sb.ToString();
    }

    /// <summary>
    /// Fila del título: azul, negrita, centrado y en mayúsculas por CSS (<c>text-transform</c>), así
    /// que el texto del composer no cambia. <paramref name="antetituloHtml"/> va encima, pequeño, y
    /// <paramref name="avisoHtml"/> (una caja de aviso ya armada) entre ambos: respeta el orden de
    /// lectura que ya tenía el correo.
    /// </summary>
    public static string Titulo(string texto, string? antetituloHtml = null, string? avisoHtml = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(texto);
        var sb = new StringBuilder(512);
        sb.Append("<tr><td style=\"padding:28px 24px 12px;text-align:center;\">");
        if (!string.IsNullOrEmpty(antetituloHtml))
        {
            sb.Append("<p style=\"margin:0 0 6px;font-size:12px;font-weight:700;letter-spacing:0.08em;text-transform:uppercase;color:")
                .Append(Muted).Append(";\">").Append(antetituloHtml).Append("</p>");
        }
        if (!string.IsNullOrEmpty(avisoHtml))
            sb.Append(avisoHtml);
        sb.Append("<h1 style=\"margin:0;font-size:22px;line-height:1.3;font-weight:700;letter-spacing:0.01em;text-transform:uppercase;color:")
            .Append(Title).Append(";\">").Append(Enc(texto)).Append("</h1></td></tr>");
        return sb.ToString();
    }

    /// <summary>Párrafo del cuerpo (HTML ya escapado), centrado por defecto como en la maqueta.</summary>
    public static string Parrafo(string innerHtml, bool centrado = true, string margen = "0 0 16px") =>
        $"<p style=\"margin:{margen};text-align:{(centrado ? "center" : "left")};\">{innerHtml}</p>";

    /// <summary>Nombre destacado del saludo (azul, negrita).</summary>
    public static string Destacado(string innerHtml) =>
        $"<strong style=\"color:{Accent};\">{innerHtml}</strong>";

    /// <summary>
    /// Tarjeta «detalle»: una fila por campo con la etiqueta ENCIMA del valor. Así se lee igual a
    /// 360 px que a 600 px sin media queries (la app de Gmail las ignora a veces). Las filas son las
    /// que aporta el composer: la tarjeta no inventa campos ni título.
    /// </summary>
    /// <param name="tituloHtml">Encabezado de la tarjeta, solo si el correo ya lo tenía en su copy.</param>
    public static string TarjetaDetalle(string? tituloHtml, IEnumerable<(string EtiquetaHtml, string ValorHtml)> filas)
    {
        ArgumentNullException.ThrowIfNull(filas);
        var lista = filas.ToList();
        var sb = new StringBuilder(1024);
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"margin:4px 0 20px;border:1px solid ")
            .Append(CardBorder).Append(";border-radius:12px;border-collapse:separate;box-shadow:0 4px 14px rgba(22,39,68,0.10);background:#ffffff;\">");
        sb.Append("<tr><td style=\"padding:8px 20px;\">");
        if (!string.IsNullOrEmpty(tituloHtml))
        {
            sb.Append("<p style=\"margin:10px 0 4px;font-size:17px;font-weight:700;text-align:center;color:")
                .Append(Ink).Append(";\">").Append(tituloHtml).Append("</p>");
        }
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\">");
        for (var i = 0; i < lista.Count; i++)
        {
            var borde = i == lista.Count - 1 ? "none" : $"1px solid {CardRowBorder}";
            sb.Append("<tr><td style=\"padding:10px 0;border-bottom:").Append(borde).Append(";text-align:left;\">")
                .Append("<p style=\"margin:0 0 2px;font-size:12px;line-height:1.4;color:").Append(Muted).Append(";\">")
                .Append(lista[i].EtiquetaHtml).Append("</p>")
                .Append("<p style=\"margin:0;font-size:15px;line-height:1.45;font-weight:600;color:").Append(Ink)
                .Append(";word-break:break-word;\">").Append(lista[i].ValorHtml).Append("</p></td></tr>");
        }
        sb.Append("</table></td></tr></table>");
        return sb.ToString();
    }

    /// <summary>
    /// Botón «bulletproof»: HTML/CSS en línea, sin imagen. Degradado del kit con <c>bgcolor</c>
    /// sólido de respaldo (Outlook no pinta degradados) y <c>mso-padding-alt</c> para que Outlook
    /// conserve el área táctil (≥ 44 px de alto).
    /// </summary>
    public static string Boton(string texto, string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(texto);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        return "<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" align=\"center\" style=\"margin:4px auto 20px;\"><tr>"
            + $"<td align=\"center\" bgcolor=\"{Accent}\" style=\"border-radius:24px;background-color:{Accent};background-image:linear-gradient(90deg,{ButtonFrom} 0%,{Accent} 100%);mso-padding-alt:14px 28px;\">"
            + $"<a href=\"{EncAttr(url)}\" target=\"_blank\" style=\"display:inline-block;padding:14px 28px;font-family:{FontStack};font-size:15px;line-height:1.2;font-weight:700;color:#ffffff;text-decoration:none;border-radius:24px;\">{Enc(texto)}</a>"
            + "</td></tr></table>";
    }

    /// <summary>Enlace de texto con el acento FLIT (para URLs visibles, que no son un botón).</summary>
    public static string Enlace(string url, string texto) =>
        $"<a href=\"{EncAttr(url)}\" target=\"_blank\" style=\"color:{Accent};font-weight:600;text-decoration:underline;word-break:break-all;\">{Enc(texto)}</a>";

    /// <summary>
    /// Pie: logo de firma, divisor y, si el correo lo trae, su texto de soporte
    /// (<paramref name="soporteHtml"/>, cada correo conserva el suyo) y la política de privacidad.
    /// Sin ninguno de los dos, solo el logo.
    /// </summary>
    public static string PieFlit(string? assetsBaseUrl, string? soporteHtml, bool politicaPrivacidad)
    {
        var sb = new StringBuilder(1024);
        sb.Append("<tr><td style=\"padding:20px 24px 8px;text-align:center;\"><img src=\"")
            .Append(EncAttr(LogoUrl(assetsBaseUrl)))
            .Append("\" alt=\"FLIT\" width=\"120\" style=\"display:inline-block;width:120px;max-width:120px;height:auto;border:0;\"/></td></tr>");
        if (string.IsNullOrEmpty(soporteHtml) && !politicaPrivacidad)
        {
            // Sin texto bajo el divisor (p. ej. la simulación de mandato), el divisor quedaría huérfano.
            sb.Append("<tr><td style=\"padding:0 24px 20px;font-size:0;line-height:0;\">&nbsp;</td></tr>");
            return sb.ToString();
        }

        sb.Append("<tr><td style=\"padding:8px 24px 28px;\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\"><tr><td style=\"border-top:1px solid ")
            .Append(Ink).Append(";font-size:0;line-height:0;height:1px;\">&nbsp;</td></tr></table>");
        if (!string.IsNullOrEmpty(soporteHtml))
        {
            sb.Append("<p style=\"margin:14px 0 10px;font-size:12px;line-height:1.55;color:").Append(Muted)
                .Append(";text-align:center;\">").Append(soporteHtml).Append("</p>");
        }
        if (politicaPrivacidad)
        {
            sb.Append("<p style=\"margin:0;text-align:center;\"><a href=\"").Append(EncAttr(PrivacyPolicyUrl))
                .Append("\" target=\"_blank\" style=\"color:").Append(Accent)
                .Append(";font-size:12px;font-weight:600;text-decoration:none;\">POLÍTICA DE PRIVACIDAD</a></p>");
        }
        sb.Append("</td></tr>");
        return sb.ToString();
    }

    /// <summary>Texto de soporte estándar del pie FLIT (el de las plantillas Security y de trámite).</summary>
    public static string SoporteEstandarHtml =>
        "Si tienes alguna pregunta o necesitas ayuda, no dudes en ponerte en contacto con nosotros. "
        + $"Puedes hacerlo a través de <strong style=\"color:{Ink};\">{Enc(SupportEmail)}</strong> o nuestro <strong style=\"color:{Ink};\">chat online</strong>.";

    public static string Enc(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    public static string EncAttr(string? value) =>
        WebUtility.HtmlEncode(value ?? string.Empty).Replace("\"", "&quot;", StringComparison.Ordinal);
}
