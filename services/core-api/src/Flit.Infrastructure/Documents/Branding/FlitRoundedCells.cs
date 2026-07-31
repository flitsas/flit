using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Flit.Infrastructure.Documents.Branding;

/// <summary>
/// Celdas tipo "tarjeta" con esquinas redondeadas para las tablas de marca FLIT (HU #10856 SOAT/RTM,
/// HU #10589 RUES). QuestPDF no tiene corner-radius nativo y su <c>.Svg()</c> llena el ancho tomando el
/// alto proporcional al viewBox (no estira al alto del contenedor). Por eso el rect redondeado va como
/// capa SVG PRINCIPAL —define el tamaño de la celda: ancho completo + alto proporcional ⇒ esquinas
/// CIRCULARES— y el contenido va encima como capa superpuesta. Cada esquina (tl/tr/br/bl) se redondea de
/// forma independiente, para conectar celdas contiguas dejando rectas las uniones internas.
/// </summary>
public static class FlitRoundedCells
{
    /// <summary>Fondo de cabecera (azul de marca #557EFF, texto blanco).</summary>
    public const string HeaderBg = FlitDocumentTheme.PrimaryBlue;

    /// <summary>Fondo de celda de valor (azul muy claro).</summary>
    public const string ValueBg = "#F4F7FC";

    /// <summary>Texto sobre la cabecera azul.</summary>
    public const string White = "#FFFFFF";

    /// <summary>viewBox estándar de una celda (alto proporcional cómodo para una línea de texto).</summary>
    public const int VbCell = 18;

    private const int R = 7; // radio de esquina en unidades de viewBox (≤ vbHeight/2)

    /// <summary>
    /// Dibuja una celda con fondo de esquinas redondeadas (las marcadas en <paramref name="tl"/>/<paramref
    /// name="tr"/>/<paramref name="br"/>/<paramref name="bl"/>) y coloca <paramref name="content"/> encima,
    /// centrado verticalmente por el consumidor. <paramref name="vbHeight"/> controla el alto proporcional
    /// de la celda (menor ⇒ más baja; útil para igualar alturas entre celdas de distinto ancho).
    /// </summary>
    public static void Cell(IContainer container, string fill, bool tl, bool tr, bool br, bool bl, int vbHeight, Action<IContainer> content) =>
        container.Layers(layers =>
        {
            layers.PrimaryLayer().Svg(RoundedRectSvg(fill, tl, tr, br, bl, vbHeight));
            content(layers.Layer());
        });

    /// <summary>
    /// SVG de un rect con las esquinas indicadas redondeadas. viewBox ancho-bajo (100×vbH); el escalado
    /// es uniforme (fill de ancho + alto proporcional) ⇒ esquinas circulares. El radio efectivo se acota
    /// a la mitad del alto del viewBox para celdas bajas.
    /// </summary>
    public static string RoundedRectSvg(string fill, bool tl, bool tr, bool br, bool bl, int vbH)
    {
        var r = Math.Min(R, vbH / 2);
        int rtl = tl ? r : 0, rtr = tr ? r : 0, rbr = br ? r : 0, rbl = bl ? r : 0;
        var d = $"M {rtl} 0 "
            + $"H {100 - rtr} "
            + (tr ? $"Q 100 0 100 {rtr} " : "L 100 0 ")
            + $"V {vbH - rbr} "
            + (br ? $"Q 100 {vbH} {100 - rbr} {vbH} " : $"L 100 {vbH} ")
            + $"H {rbl} "
            + (bl ? $"Q 0 {vbH} 0 {vbH - rbl} " : $"L 0 {vbH} ")
            + $"V {rtl} "
            + (tl ? $"Q 0 0 {rtl} 0 " : "L 0 0 ")
            + "Z";
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"{vbH}\" viewBox=\"0 0 100 {vbH}\">"
            + $"<path d=\"{d}\" fill=\"{fill}\"/></svg>";
    }
}
