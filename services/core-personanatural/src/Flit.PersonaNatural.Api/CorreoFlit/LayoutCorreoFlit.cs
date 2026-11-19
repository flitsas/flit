using System.Net;
using System.Text;

namespace Flit.PersonaNatural.Api.CorreoFlit;

/// <summary>
/// Marco FLIT de los correos del portal (Correos 2.0): banner, título azul en mayúsculas, cuerpo, tarjeta de detalle y pie con
/// logo y divisor. Copia local del layout FLIT de core-api (<c>FlitBrandedEmailLayout</c>): este es un servicio aparte y no
/// depende de otro. Solo pone el marco; el texto lo trae cada correo y no cambia. El pie NO lleva el texto de soporte ni la
/// política de privacidad: estos correos nunca lo tuvieron y el copy está congelado.
/// <para>
/// CSS en línea (Gmail descarta <c>&lt;style&gt;</c>) y sin anchos fijos: la tabla mide 100 % hasta 600 px, así que a 360 px no
/// hay desborde horizontal. Las imágenes van por URL absoluta desde <see cref="ClaveBaseUrl"/>, la misma clave que usa core-api.
/// </para>
/// </summary>
internal static class LayoutCorreoFlit
{
    /// <summary>Misma clave que core-api: en los ambientes, <c>Notifications__EmailAssets__BaseUrl</c> = <c>{front}/email-assets</c>.</summary>
    public const string ClaveBaseUrl = "Notifications:EmailAssets:BaseUrl";

    /// <summary>Respaldo SOLO para desarrollo local (el mismo de core-api); los ambientes definen <see cref="ClaveBaseUrl"/>.</summary>
    public const string BaseUrlPorDefecto = "http://localhost:3000/email-assets";

    public const string Banner = "flit-banner-correo.png";
    public const string LogoFirma = "flit-logo-firma.png";

    private const string Azul = "#557EFF";
    private const string AzulOscuro = "#162744";
    private const string Tinta = "#162744";
    private const string Gris = "#59677D";
    private const string Borde = "#E3E8F2";
    private const string FondoFila = "#F3F6FD";
    private const string Fuente = "Poppins,Arial,Helvetica,sans-serif";

    /// <summary>Base de los assets desde la configuración; sin valor, el respaldo local.</summary>
    public static string BaseDesde(IConfiguration configuracion) => Normalizar(configuracion[ClaveBaseUrl]);

    public static string Normalizar(string? baseUrl) =>
        string.IsNullOrWhiteSpace(baseUrl) ? BaseUrlPorDefecto : baseUrl.Trim().TrimEnd('/');

    /// <summary>Envuelve el cuerpo (HTML ya codificado) con el marco FLIT. <paramref name="titulo"/> es texto plano.</summary>
    public static string Envolver(string titulo, string cuerpoHtml, string? baseUrl)
    {
        var b = Normalizar(baseUrl);
        var sb = new StringBuilder(4096);
        sb.Append("<!DOCTYPE html><html lang=\"es\"><head><meta charset=\"utf-8\"/>")
            .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"/>")
            .Append("<meta name=\"color-scheme\" content=\"light\"/><meta name=\"supported-color-schemes\" content=\"light\"/></head>")
            .Append($"<body style=\"margin:0;padding:0;background:#ffffff;font-family:{Fuente};color:{Tinta};\">")
            .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"width:100%;max-width:600px;margin:0 auto;background:#ffffff;border-collapse:collapse;\">")
            .Append($"<tr><td style=\"padding:0;\"><img src=\"{Attr(b + "/" + Banner)}\" alt=\"FLIT Versión 2.0\" width=\"600\" style=\"display:block;width:100%;max-width:600px;height:auto;border:0;\"/></td></tr>")
            .Append($"<tr><td style=\"padding:28px 20px 4px;text-align:center;\"><h1 style=\"margin:0;font-family:{Fuente};font-size:20px;line-height:1.35;font-weight:700;letter-spacing:0.02em;text-transform:uppercase;color:{Azul};\">{Enc(titulo)}</h1></td></tr>")
            .Append($"<tr><td style=\"padding:16px 20px 8px;text-align:center;font-size:15px;line-height:1.6;color:{Tinta};\">{cuerpoHtml}</td></tr>")
            .Append($"<tr><td style=\"padding:20px 20px 16px;text-align:center;\"><img src=\"{Attr(b + "/" + LogoFirma)}\" alt=\"FLIT\" width=\"120\" style=\"display:inline-block;width:120px;max-width:120px;height:auto;border:0;\"/></td></tr>")
            .Append($"<tr><td style=\"padding:0 20px 28px;\"><hr style=\"border:none;border-top:1px solid {Azul};margin:0;\"/></td></tr>")
            .Append("</table></body></html>");
        return sb.ToString();
    }

    /// <summary>Párrafo del cuerpo con HTML interno ya codificado.</summary>
    public static string Parrafo(string html) => $"<p style=\"margin:0 0 12px;\">{html}</p>";

    /// <summary>
    /// Saludo del correo. Con nombre, «Hola» y debajo el nombre en negrita azul con la coma (como la maqueta); sin nombre,
    /// «Hola,». Las palabras son las mismas en ambos casos: solo cambia el salto de línea.
    /// </summary>
    public static string Saludo(string? nombre) =>
        string.IsNullOrWhiteSpace(nombre)
            ? Parrafo("Hola,")
            : Parrafo($"Hola<br><strong style=\"color:{Azul};\">{Enc(nombre.Trim())},</strong>");

    /// <summary>Nota secundaria (gris, más pequeña) con HTML interno ya codificado.</summary>
    public static string Nota(string html) => $"<p style=\"margin:12px 0 0;font-size:13px;line-height:1.5;color:{Gris};\">{html}</p>";

    /// <summary>Resaltado del cuerpo (código, organismo).</summary>
    public static string Destacado(string texto) => $"<strong style=\"color:{AzulOscuro};\">{Enc(texto)}</strong>";

    /// <summary>
    /// Tarjeta de detalle con filas etiqueta/valor (la etiqueta puede llevar una segunda línea) y una fila final destacada.
    /// Dos columnas que se ajustan: la etiqueta envuelve y el valor no se parte, así se lee a 360 px.
    /// </summary>
    public static string Tarjeta(IEnumerable<(string Etiqueta, string? Detalle, string Valor)> filas, (string Etiqueta, string Valor)? total)
    {
        var sb = new StringBuilder();
        sb.Append($"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"width:100%;margin:8px 0 4px;border:1px solid {Borde};border-radius:12px;border-collapse:separate;box-shadow:0 2px 8px rgba(22,39,68,0.08);text-align:left;\">");
        var i = 0;
        foreach (var (etiqueta, detalle, valor) in filas)
        {
            var fondo = i++ % 2 == 0 ? FondoFila : "#ffffff";
            var segunda = string.IsNullOrWhiteSpace(detalle) ? string.Empty : $"<br><span style=\"font-size:12px;color:{Gris};\">{Enc(detalle)}</span>";
            sb.Append($"<tr><td style=\"padding:10px 12px;background:{fondo};font-size:14px;line-height:1.4;color:{Tinta};\">{Enc(etiqueta)}{segunda}</td>")
                .Append($"<td style=\"padding:10px 12px;background:{fondo};font-size:14px;line-height:1.4;color:{Tinta};text-align:right;white-space:nowrap;vertical-align:top;\">{Enc(valor)}</td></tr>");
        }

        if (total is { } t)
        {
            sb.Append($"<tr><td style=\"padding:12px;border-top:1px solid {Borde};font-size:15px;font-weight:700;color:{AzulOscuro};\">{Enc(t.Etiqueta)}</td>")
                .Append($"<td style=\"padding:12px;border-top:1px solid {Borde};font-size:15px;font-weight:700;color:{AzulOscuro};text-align:right;white-space:nowrap;\">{Enc(t.Valor)}</td></tr>");
        }

        return sb.Append("</table>").ToString();
    }

    private static string Enc(string texto) => WebUtility.HtmlEncode(texto);

    private static string Attr(string texto) => WebUtility.HtmlEncode(texto);
}
