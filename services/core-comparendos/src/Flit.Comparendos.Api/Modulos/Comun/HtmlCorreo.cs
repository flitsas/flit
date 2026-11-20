using System.Net;
using System.Text;
using Flit.Comparendos.Domain.Modelo;

namespace Flit.Comparendos.Api.Modulos.Comun;

/// <summary>Marco HTML sencillo y compatible con clientes de correo para los avisos internos de FLIT (alertas, reportes).</summary>
public static class HtmlCorreo
{
    /// <param name="marcaFlit">false para las comunicaciones del conjunto Renting: ni el color ni el pie mencionan a FLIT
    /// (D9, 2026-10-13: como en Trámites, la empresa le escribe a su gente sin rastro de FLIT).</param>
    /// <param name="recursos">Correos 2.0: URL pública de las imágenes de correo; con ella el marco FLIT lleva banner y logo.</param>
    public static string Envolver(string titulo, string cuerpoHtml, string? pie = null, bool marcaFlit = true, string? recursos = null)
    {
        if (!marcaFlit)
            return EnvolverNeutro(titulo, cuerpoHtml, pie);

        // Correos 2.0: banner, título azul en mayúsculas, cuerpo y pie con logo y divisor (el texto del pie es el de siempre).
        var sb = new StringBuilder();
        AbrirFlit(sb);
        if (recursos is { Length: > 0 })
            Imagen(sb, $"{recursos}/flit-banner-correo.png", "FLIT Versión 2.0");
        sb.Append("<tr><td style=\"padding:28px 32px 8px\">").Append(Titulo(titulo)).Append(cuerpoHtml).Append("</td></tr>");
        sb.Append("<tr><td style=\"padding:16px 32px 32px;text-align:center\">");
        if (recursos is { Length: > 0 })
            Logo(sb, recursos);
        sb.Append("<hr style=\"border:none;border-top:1px solid ").Append(Azul).Append(";margin:0 0 14px\"/>");
        sb.Append("<p style=\"margin:0;font-size:12px;line-height:1.5;color:").Append(Gris).Append(";text-align:center\">")
            .Append(WebUtility.HtmlEncode(pie ?? "FLIT · Gestión de Comparendos. Correo automático, no lo respondas.")).Append("</p>");
        sb.Append("</td></tr>");
        CerrarFlit(sb);
        return sb.ToString();
    }

    /// <summary>El marco sencillo sin marca (Renting, D9): igual que antes de Correos 2.0.</summary>
    private static string EnvolverNeutro(string titulo, string cuerpoHtml, string? pie)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><body style=\"margin:0;padding:0;background:#F4F6FA;font-family:Arial,Helvetica,sans-serif;color:#162744\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\"><tr><td align=\"center\" style=\"padding:24px\">");
        sb.Append("<table role=\"presentation\" width=\"640\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#FFFFFF;border-radius:12px;border:1px solid #DFE5ED\">");
        sb.Append("<tr><td style=\"background:#3A4250;color:#FFFFFF;padding:18px 24px;border-radius:12px 12px 0 0;font-size:18px;font-weight:bold\">")
            .Append(WebUtility.HtmlEncode(titulo)).Append("</td></tr>");
        sb.Append("<tr><td style=\"padding:24px;font-size:14px;line-height:1.5\">").Append(cuerpoHtml).Append("</td></tr>");
        sb.Append("<tr><td style=\"padding:16px 24px;font-size:12px;color:#5B6B82;border-top:1px solid #DFE5ED\">")
            .Append(WebUtility.HtmlEncode(pie ?? "Correo automático, no lo respondas.")).Append("</td></tr>");
        sb.Append("</table></td></tr></table></body></html>");
        return sb.ToString();
    }

    /// <summary>
    /// HU #13883: marco de los avisos de Renting, el de V1. Cabecera «¡Aviso! Fotomulta pendiente de pago» y pie de contacto
    /// en imagen, texto en Arial 14px y modo claro forzado. Nada de FLIT.
    /// </summary>
    /// <param name="recursos">URL pública de las imágenes de correo (frontend de Comparendos, /assets/correo).</param>
    public static string Renting(string cuerpoHtml, string recursos)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"/><meta name=\"color-scheme\" content=\"light\"/><meta name=\"supported-color-schemes\" content=\"light\"/></head>");
        sb.Append("<body style=\"margin:0;padding:0;background:#ffffff;font-family:Arial,Helvetica,sans-serif;color:#333333;font-size:14px;line-height:1.5\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:640px;margin:0 auto;background:#ffffff\">");
        Imagen(sb, $"{recursos}/renting-cabecera.jpg", "¡Aviso! Fotomulta pendiente de pago · Renting Colombia y Localiza");
        sb.Append("<tr><td style=\"padding:32px 36px 16px;background:#ffffff\">").Append(cuerpoHtml).Append("</td></tr>");
        Imagen(sb, $"{recursos}/renting-pie.jpg", "¿Tienes dudas o necesitas ayuda? tramitesvehiculos@rentingcolombia.com · Renting Colombia y Localiza");
        sb.Append("</table></body></html>");
        return sb.ToString();
    }

    /// <summary>
    /// HU #13883: marco de los avisos de las gestoras con FLIT, el de los correos de Trámites: banner arriba, firma con el
    /// nombre de la empresa y el logo de FLIT, y pie de soporte. El título va en la plantilla, para que se pueda editar.
    /// </summary>
    /// <param name="cierre">false cuando la plantilla trae su propia firma (el correo al organismo, firmado por el representante).</param>
    public static string Flit(string cuerpoHtml, string? empresa, string recursos, bool cierre = true)
    {
        // Correos 2.0: banner nuevo, logo de firma centrado, divisor azul, soporte y política de privacidad (mismo texto).
        var sb = new StringBuilder();
        AbrirFlit(sb);
        Imagen(sb, $"{recursos}/flit-banner-correo.png", "FLIT Versión 2.0");
        sb.Append("<tr><td style=\"padding:28px 32px 8px\">").Append(cuerpoHtml);
        if (cierre)
        {
            sb.Append("<p style=\"margin:20px 0 4px\">Cordialmente,</p>");
            if (empresa is { Length: > 0 })
                sb.Append("<p style=\"margin:0\"><strong>").Append(WebUtility.HtmlEncode(empresa)).Append("</strong></p>");
        }

        sb.Append("</td></tr><tr><td style=\"padding:16px 32px 32px;text-align:center\">");
        Logo(sb, recursos);
        sb.Append("<hr style=\"border:none;border-top:1px solid ").Append(Azul).Append(";margin:0 0 14px\"/>");
        sb.Append("<p style=\"margin:0 0 10px;font-size:12px;line-height:1.5;color:").Append(Gris).Append(";text-align:center\">");
        sb.Append(empresa is { Length: > 0 } ? $"Este correo lo envía {WebUtility.HtmlEncode(empresa)} con FLIT. " : string.Empty);
        sb.Append("Si necesitas ayuda escribe a <strong style=\"color:").Append(Tinta).Append("\">soporte@flitsas.com</strong>.</p>");
        sb.Append("<p style=\"margin:0;text-align:center\"><a href=\"https://flitsas.com.co/privacy-policy/\" style=\"color:").Append(Azul)
            .Append(";font-size:11px;font-weight:600;letter-spacing:0.02em;text-decoration:none\">POLÍTICA DE PRIVACIDAD</a></p>");
        sb.Append("</td></tr>");
        CerrarFlit(sb);
        return sb.ToString();
    }

    /// <summary>
    /// HU #14621: el marco de un correo del sistema a la gente de la empresa según el conjunto de plantillas de la empresa (el canal
    /// FlitSmtp/Renting solo es el transporte): con la cabecera de FLIT, o el marco neutro sin rastro de FLIT en Renting (su cabecera
    /// es la de los avisos al conductor y no sirve para esto).
    /// </summary>
    public static string DeEmpresa(ConjuntoPlantillas conjunto, string titulo, string cuerpoHtml, string? empresa, string recursos)
    {
        if (conjunto == ConjuntoPlantillas.Renting)
            return RentingSistema($"<h2 style=\"margin:0 0 12px;font-size:17px\">{WebUtility.HtmlEncode(titulo)}</h2>{cuerpoHtml}", empresa, recursos);
        return Flit($"{Titulo(titulo)}{cuerpoHtml}", empresa, recursos, cierre: false);
    }

    /// <summary>
    /// HU #14621: marco de Renting para los correos de DP y del sistema a organismos y a la empresa: la cabecera de Renting SIN el texto
    /// «¡Aviso! Fotomulta pendiente de pago» (<c>renting-cabecera-dp.jpg</c>, provisional hasta que Diseño entregue la suya) y, en vez del
    /// pie de dudas de los avisos al conductor, un pie corto y neutro. Nada de FLIT. Los avisos al conductor siguen con <see cref="Renting"/>.
    /// </summary>
    public static string RentingSistema(string cuerpoHtml, string? empresa, string recursos)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"/><meta name=\"color-scheme\" content=\"light\"/><meta name=\"supported-color-schemes\" content=\"light\"/></head>");
        sb.Append("<body style=\"margin:0;padding:0;background:#ffffff;font-family:Arial,Helvetica,sans-serif;color:#333333;font-size:14px;line-height:1.5\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:640px;margin:0 auto;background:#ffffff\">");
        Imagen(sb, $"{recursos}/renting-cabecera-dp.jpg", "Renting Colombia · Localiza");
        sb.Append("<tr><td style=\"padding:32px 36px 16px;background:#ffffff\">").Append(cuerpoHtml).Append("</td></tr>");
        sb.Append("<tr><td style=\"padding:8px 36px 28px\"><hr style=\"border:none;border-top:1px solid #E5EAF2;margin:0 0 12px\"/><p style=\"margin:0;font-size:12px;color:#6B778C;text-align:center\">")
            .Append(empresa is { Length: > 0 } ? $"{WebUtility.HtmlEncode(empresa)} · " : string.Empty).Append("Correo automático, no lo respondas.</p></td></tr>");
        sb.Append("</table></body></html>");
        return sb.ToString();
    }

    private static void Imagen(StringBuilder sb, string url, string alt) =>
        sb.Append("<tr><td style=\"padding:0;line-height:0;background:#ffffff\"><img src=\"").Append(WebUtility.HtmlEncode(url)).Append("\" alt=\"")
            .Append(WebUtility.HtmlEncode(alt)).Append("\" width=\"640\" style=\"display:block;width:100%;max-width:640px;height:auto;border:0\"/></td></tr>");

    /// <summary>
    /// Correos 2.0: las filas etiqueta/valor de alertas y reportes en una tarjeta (borde redondeado, sombra suave y filas
    /// alternas) que se lee bien en un teléfono: la etiqueta a la izquierda y el valor, que puede partirse, a la derecha.
    /// </summary>
    /// <param name="marcaFlit">false en los reportes del conjunto Renting: conservan la tabla sencilla de antes, sin el estilo de FLIT.</param>
    public static string Tabla(IEnumerable<(string Etiqueta, string? Valor)> filas, bool marcaFlit = true)
    {
        ArgumentNullException.ThrowIfNull(filas);
        if (!marcaFlit)
            return TablaSencilla(filas);
        var sb = new StringBuilder("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"width:100%;margin:16px 0;border-collapse:separate;");
        sb.Append("background:#FFFFFF;border:1px solid ").Append(Borde).Append(";border-radius:12px;box-shadow:0 2px 8px rgba(22,39,68,0.12)\"><tr><td style=\"padding:12px\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse;width:100%;font-size:13px;line-height:1.45\">");
        var par = false;
        foreach (var (etiqueta, valor) in filas)
        {
            var fondo = par ? "#FFFFFF" : "#F1F5FD";
            sb.Append("<tr><td style=\"padding:8px 10px;background:").Append(fondo).Append(";color:").Append(Gris).Append(";width:40%;vertical-align:top;font-weight:600\">")
                .Append(WebUtility.HtmlEncode(etiqueta))
                .Append("</td><td style=\"padding:8px 10px;background:").Append(fondo).Append(";color:").Append(Tinta).Append(";vertical-align:top;word-break:break-word\">")
                .Append(WebUtility.HtmlEncode(valor ?? "—")).Append("</td></tr>");
            par = !par;
        }

        return sb.Append("</table></td></tr></table>").ToString();
    }

    private static string TablaSencilla(IEnumerable<(string Etiqueta, string? Valor)> filas)
    {
        var sb = new StringBuilder("<table cellpadding=\"6\" cellspacing=\"0\" style=\"border-collapse:collapse;width:100%;font-size:13px\">");
        foreach (var (etiqueta, valor) in filas)
        {
            sb.Append("<tr><td style=\"border-bottom:1px solid #EEF1F6;color:#5B6B82;width:40%\">").Append(WebUtility.HtmlEncode(etiqueta))
                .Append("</td><td style=\"border-bottom:1px solid #EEF1F6\">").Append(WebUtility.HtmlEncode(valor ?? "—")).Append("</td></tr>");
        }

        return sb.Append("</table>").ToString();
    }

    /// <summary>
    /// Correos 2.0: botón «a prueba de balas» en HTML/CSS (no imagen, para que el texto sea el del código): degradado de la marca
    /// con color sólido de respaldo para Outlook, que no pinta degradados.
    /// </summary>
    public static string Boton(string texto, string url) =>
        "<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin:16px auto\"><tr><td align=\"center\" bgcolor=\"" + Azul
        + "\" style=\"border-radius:22px;background:" + Azul + ";background-image:linear-gradient(90deg," + Turquesa + "," + Azul + ");mso-padding-alt:11px 24px\"><a href=\""
        + WebUtility.HtmlEncode(url) + "\" target=\"_blank\" style=\"display:inline-block;padding:11px 24px;font-family:Poppins,Arial,Helvetica,sans-serif;font-size:14px;"
        + "font-weight:600;color:#FFFFFF;text-decoration:none;border-radius:22px\">" + WebUtility.HtmlEncode(texto) + "</a></td></tr></table>";

    // ── Piezas del marco FLIT (Correos 2.0) ─────────────────────────────────────────────────────────────────────────────
    /// <summary>El azul de la marca según Diseño (el de sus SVG): títulos, divisor, enlaces, política y botón.</summary>
    private const string Azul = "#557EFF";
    /// <summary>El turquesa de la marca (SVG de Diseño): inicio del degradado del botón.</summary>
    private const string Turquesa = "#00DBD5";
    private const string Tinta = "#162744";
    private const string Gris = "#6B778C";
    private const string Borde = "#E5EAF2";

    private static void AbrirFlit(StringBuilder sb)
    {
        sb.Append("<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"/><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"/>");
        sb.Append("<meta name=\"color-scheme\" content=\"light\"/><meta name=\"supported-color-schemes\" content=\"light\"/></head>");
        sb.Append("<body style=\"margin:0;padding:0;background:#ffffff;font-family:Poppins,Arial,Helvetica,sans-serif;color:").Append(Tinta).Append(";font-size:14px;line-height:1.55\">");
        // Outlook de escritorio no respeta max-width: le fija los 640 px con una tabla condicional.
        sb.Append("<!--[if mso]><table role=\"presentation\" width=\"640\" align=\"center\" cellpadding=\"0\" cellspacing=\"0\"><tr><td><![endif]-->");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:640px;margin:0 auto;background:#ffffff\">");
    }

    private static void CerrarFlit(StringBuilder sb) => sb.Append("</table><!--[if mso]></td></tr></table><![endif]--></body></html>");

    /// <summary>Título azul, centrado y en mayúsculas por CSS: el texto sigue siendo el mismo.</summary>
    private static string Titulo(string titulo) =>
        $"<h1 style=\"margin:0 0 20px;font-size:19px;line-height:1.3;font-weight:700;color:{Azul};text-align:center;text-transform:uppercase;letter-spacing:0.02em\">{WebUtility.HtmlEncode(titulo)}</h1>";

    private static void Logo(StringBuilder sb, string recursos) =>
        sb.Append("<img src=\"").Append(WebUtility.HtmlEncode($"{recursos}/flit-logo-firma.png")).Append("\" alt=\"FLIT\" width=\"120\" height=\"37\"")
            .Append(" style=\"display:block;margin:0 auto 20px;border:0;width:120px;height:auto\"/>");

    /// <summary>Documento de persona enmascarado en correos y logs (RN-011): «****5678».</summary>
    public static string Enmascarar(string? documento) =>
        string.IsNullOrEmpty(documento) ? "—" : documento.Length <= 4 ? "****" : $"****{documento[^4..]}";
}
