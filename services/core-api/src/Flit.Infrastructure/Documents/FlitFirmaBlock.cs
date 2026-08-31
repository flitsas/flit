using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Flit.Infrastructure.Documents;

/// <summary>Estilo de la línea de firma según el documento.</summary>
internal enum FlitFirmaLinea
{
    /// <summary>Guiones bajos, como el contrato de mandato legacy.</summary>
    Underscores,

    /// <summary>Línea gráfica fina, como la solicitud de trámite virtual.</summary>
    Grafica,
}

/// <summary>
/// Bloque de firma común al contrato de mandato y a la solicitud de trámite virtual (HU #11046).
///
/// <para><b>Composición.</b> La estampa va <b>sobre</b> la línea de firma, y los datos del firmante
/// debajo de ella:</para>
/// <code>
///   [imagen del baúl  ó  sello de validación de identidad]
///   [vigencia y hash, si la estampa es la del baúl (HU #11170)]
///   ─────────────────────────────────────────────────────
///   EMPRESA / NIT / NOMBRE / documento / CELULAR / CORREO
/// </code>
///
/// <para>Antes cada generador pintaba <i>o</i> la imagen <i>o</i> la línea (nunca las dos) y colocaba el
/// sello de identidad DEBAJO del bloque de datos, así que la firma no quedaba sobre la línea y el
/// documento no se leía como firmado.</para>
///
/// <para><b>Prioridad del baúl (HU #11031), que este helper centraliza.</b> Si la parte tiene firma
/// vigente en el baúl, esa es la firma del documento y NO se añade además el sello de la validación de
/// identidad: pintar ambos dejaba el documento como si la parte hubiera firmado de dos maneras
/// distintas. Esta HU solo cambia la POSICIÓN; la exclusividad se conserva.</para>
/// </summary>
/// <summary>Qué se estampa sobre la línea de firma.</summary>
internal enum FlitEstampa
{
    /// <summary>Imagen de la firma del baúl (gana sobre el sello, HU #11031).</summary>
    Baul,

    /// <summary>Sello de la validación biométrica de identidad.</summary>
    SelloIdentidad,

    /// <summary>Ninguna: se deja el espacio para la firma manuscrita.</summary>
    Ninguna,
}

internal static class FlitFirmaBlock
{
    /// <summary>
    /// Decide qué se estampa sobre la línea. Es la <b>prioridad del baúl</b> de la HU #11031 en una
    /// función pura: con firma del baúl vigente, esa es la firma del documento y el sello de la
    /// validación de identidad NO se añade (pintar ambos dejaba el documento como si la parte hubiera
    /// firmado de dos maneras distintas).
    /// </summary>
    internal static FlitEstampa ResolverEstampa(byte[]? firmaBaul, string? selloIdentidad)
    {
        if (firmaBaul is { Length: > 0 })
            return FlitEstampa.Baul;
        return string.IsNullOrWhiteSpace(selloIdentidad) ? FlitEstampa.Ninguna : FlitEstampa.SelloIdentidad;
    }

    /// <summary>Alto de la imagen de firma del baúl, en puntos.</summary>
    private const float ImagenAlto = 32f;

    /// <summary>Ancho de la línea gráfica, en puntos.</summary>
    private const float LineaAncho = 240f;

    /// <summary>Guiones bajos de la línea del mandato (ancho heredado de la plantilla legacy).</summary>
    private const string LineaUnderscores = "____________________________";

    /// <param name="firmaBaul">Imagen de la firma del baúl resuelta para la parte; null/vacía si no tiene.</param>
    /// <param name="selloIdentidad">
    /// Sello de la validación biométrica de identidad (multi-línea). Se pinta SOLO si no hay firma del
    /// baúl (prioridad del baúl, HU #11031).
    /// </param>
    /// <param name="datos">Líneas de identificación del firmante, que van bajo la línea.</param>
    /// <param name="selloFontSize">Cuerpo del sello de identidad.</param>
    /// <param name="datosBold">La solicitud virtual imprime su bloque en negrita; el mandato no.</param>
    /// <param name="selloBaul">
    /// HU #11170 — vigencia y hash de la firma del baúl (<see cref="FlitFirmaBaulSello"/>), que van
    /// bajo la imagen. Sin ellos, la imagen es un trazo sin nada que permita verificarla: el FUR sí los
    /// estampaba y estos documentos no. Se ignora si la estampa no es la del baúl.
    /// </param>
    internal static void Render(
        ColumnDescriptor col,
        byte[]? firmaBaul,
        string? selloIdentidad,
        IEnumerable<string> datos,
        FlitFirmaLinea linea,
        float selloFontSize = 6.5f,
        bool datosBold = false,
        string? selloBaul = null,
        string? etiquetaSinEstampa = null)
    {
        ArgumentNullException.ThrowIfNull(col);
        ArgumentNullException.ThrowIfNull(datos);

        // 1. Estampa SOBRE la línea. Sin firma ni sello se deja el aire para la firma manuscrita, de modo
        //    que la línea no suba y el bloque conserve su altura en cualquier caso.
        switch (ResolverEstampa(firmaBaul, selloIdentidad))
        {
            case FlitEstampa.Baul:
                col.Item().Height(ImagenAlto).Image(firmaBaul!).FitHeight();
                // La trazabilidad de la firma custodiada acompaña a la imagen, no a los datos del
                // firmante: si el documento se lee sin ella, la imagen no se puede verificar.
                RenderSello(col, selloBaul, selloFontSize);
                break;

            case FlitEstampa.SelloIdentidad:
                RenderSello(col, selloIdentidad, selloFontSize);
                break;

            default:
                if (!string.IsNullOrWhiteSpace(etiquetaSinEstampa))
                {
                    col.Item().Height(ImagenAlto).AlignMiddle().Text(t =>
                        t.Span(etiquetaSinEstampa.Trim()).FontSize(9).FontColor(Colors.Grey.Darken2));
                }
                else
                {
                    col.Item().Height(ImagenAlto);
                }
                break;
        }

        // 2. La línea, SIEMPRE presente.
        if (linea == FlitFirmaLinea.Grafica)
            col.Item().PaddingBottom(4).Width(LineaAncho).LineHorizontal(0.5f);
        else
            col.Item().Text(LineaUnderscores);

        // 3. Datos del firmante, bajo la línea.
        foreach (var line in datos)
        {
            col.Item().Text(t =>
            {
                var span = t.Span(line).FontSize(10);
                if (datosBold)
                    span.Bold();
            });
        }
    }

    /// <summary>Sello multilínea en gris pequeño (trazabilidad del baúl o de la identidad).</summary>
    private static void RenderSello(ColumnDescriptor col, string? sello, float fontSize)
    {
        if (string.IsNullOrWhiteSpace(sello))
            return;

        col.Item().Column(c =>
        {
            foreach (var line in sello.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                c.Item().Text(t => t
                    .Span(line.Trim())
                    .FontSize(fontSize)
                    .FontColor(Colors.Grey.Darken2));
            }
        });
    }
}
