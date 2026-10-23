using System.Net;
using System.Text;

namespace Flit.Comparendos.Api.Modulos.Comun;

/// <summary>Marco HTML sencillo y compatible con clientes de correo para los avisos internos de FLIT (alertas, reportes).</summary>
public static class HtmlCorreo
{
    /// <param name="marcaFlit">false para las comunicaciones del conjunto Renting: ni el color ni el pie mencionan a FLIT
    /// (D9, 2026-10-13: como en Trámites, la empresa le escribe a su gente sin rastro de FLIT).</param>
    public static string Envolver(string titulo, string cuerpoHtml, string? pie = null, bool marcaFlit = true)
    {
        var encabezado = marcaFlit ? "#162744" : "#3A4250";
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><body style=\"margin:0;padding:0;background:#F4F6FA;font-family:Arial,Helvetica,sans-serif;color:#162744\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\"><tr><td align=\"center\" style=\"padding:24px\">");
        sb.Append("<table role=\"presentation\" width=\"640\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#FFFFFF;border-radius:12px;border:1px solid #DFE5ED\">");
        sb.Append("<tr><td style=\"background:").Append(encabezado).Append(";color:#FFFFFF;padding:18px 24px;border-radius:12px 12px 0 0;font-size:18px;font-weight:bold\">")
            .Append(WebUtility.HtmlEncode(titulo)).Append("</td></tr>");
        sb.Append("<tr><td style=\"padding:24px;font-size:14px;line-height:1.5\">").Append(cuerpoHtml).Append("</td></tr>");
        sb.Append("<tr><td style=\"padding:16px 24px;font-size:12px;color:#5B6B82;border-top:1px solid #DFE5ED\">")
            .Append(WebUtility.HtmlEncode(pie ?? (marcaFlit ? "FLIT · Gestión de Comparendos. Correo automático, no lo respondas." : "Correo automático, no lo respondas."))).Append("</td></tr>");
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
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"/></head>");
        sb.Append("<body style=\"margin:0;padding:0;background:#ffffff;font-family:Arial,Helvetica,sans-serif;color:#162244;font-size:14px;line-height:1.55\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:640px;margin:0 auto;background:#ffffff\">");
        Imagen(sb, $"{recursos}/flit-cabecera.png", "FLIT Versión 2.0");
        sb.Append("<tr><td style=\"padding:24px 28px 8px\">").Append(cuerpoHtml);
        if (cierre)
        {
            sb.Append("<p style=\"margin:20px 0 4px\">Cordialmente,</p>");
            if (empresa is { Length: > 0 })
                sb.Append("<p style=\"margin:0\"><strong>").Append(WebUtility.HtmlEncode(empresa)).Append("</strong></p>");
        }

        sb.Append("<img src=\"").Append(WebUtility.HtmlEncode($"{recursos}/flit-logo.png")).Append("\" alt=\"flit\" width=\"72\" style=\"display:block;border:0;margin:12px 0 0\"/>");
        sb.Append("</td></tr><tr><td style=\"padding:8px 28px 28px\"><hr style=\"border:none;border-top:1px solid #E5EAF2;margin:0 0 14px\"/>");
        sb.Append("<p style=\"margin:0 0 10px;font-size:12px;line-height:1.5;color:#6B778C;text-align:center\">");
        sb.Append(empresa is { Length: > 0 } ? $"Este correo lo envía {WebUtility.HtmlEncode(empresa)} con FLIT. " : string.Empty);
        sb.Append("Si necesitas ayuda escribe a <strong>soporte@flitsas.com</strong>.</p>");
        sb.Append("<p style=\"margin:0;text-align:center\"><a href=\"https://flitsas.com.co/privacy-policy/\" style=\"color:#2F6FED;font-size:12px;text-decoration:underline\">POLÍTICA DE PRIVACIDAD</a></p>");
        sb.Append("</td></tr></table></body></html>");
        return sb.ToString();
    }

    private static void Imagen(StringBuilder sb, string url, string alt) =>
        sb.Append("<tr><td style=\"padding:0;line-height:0;background:#ffffff\"><img src=\"").Append(WebUtility.HtmlEncode(url)).Append("\" alt=\"")
            .Append(WebUtility.HtmlEncode(alt)).Append("\" width=\"640\" style=\"display:block;width:100%;max-width:640px;height:auto;border:0\"/></td></tr>");

    public static string Tabla(IEnumerable<(string Etiqueta, string? Valor)> filas)
    {
        ArgumentNullException.ThrowIfNull(filas);
        var sb = new StringBuilder("<table cellpadding=\"6\" cellspacing=\"0\" style=\"border-collapse:collapse;width:100%;font-size:13px\">");
        foreach (var (etiqueta, valor) in filas)
        {
            sb.Append("<tr><td style=\"border-bottom:1px solid #EEF1F6;color:#5B6B82;width:40%\">").Append(WebUtility.HtmlEncode(etiqueta))
                .Append("</td><td style=\"border-bottom:1px solid #EEF1F6\">").Append(WebUtility.HtmlEncode(valor ?? "—")).Append("</td></tr>");
        }

        return sb.Append("</table>").ToString();
    }

    /// <summary>Documento de persona enmascarado en correos y logs (RN-011): «****5678».</summary>
    public static string Enmascarar(string? documento) =>
        string.IsNullOrEmpty(documento) ? "—" : documento.Length <= 4 ? "****" : $"****{documento[^4..]}";
}
