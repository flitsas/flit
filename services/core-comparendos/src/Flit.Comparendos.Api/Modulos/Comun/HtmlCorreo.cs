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
