using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Flit.Diagnostico.Api.Modulos.Reportes.Marca;

/// <summary>
/// Bandas del membrete FLIT para <c>page.Header()</c> / <c>page.Footer()</c> de QuestPDF (HU #13848). Copia de
/// <c>services/core-api/src/Flit.Infrastructure/Documents/Branding/FlitLetterhead.cs</c> (HU #10855): cada banda ocupa el alto
/// del margen (2,54 cm) con el SVG de la hoja.
/// </summary>
internal static class FlitLetterhead
{
    /// <summary>Banda superior del membrete (SVG).</summary>
    public static void Header(IContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);
        container.Height(FlitDocumentTheme.MarginCm, Unit.Centimetre).AlignTop().Svg(BrandingAssets.MembreteHeaderSvg);
    }

    /// <summary>Banda inferior del membrete (SVG).</summary>
    public static void Footer(IContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);
        container.Height(FlitDocumentTheme.MarginCm, Unit.Centimetre).AlignBottom().Svg(BrandingAssets.MembreteFooterSvg);
    }

    /// <summary>Carta, sin margen, Poppins 9 pt en azul oscuro (el membrete y el contenido los pone quien llama).</summary>
    public static void ApplyTo(PageDescriptor page)
    {
        ArgumentNullException.ThrowIfNull(page);
        page.Size(FlitDocumentTheme.Page);
        page.Margin(0);
        page.DefaultTextStyle(t => t.FontFamily(FlitDocumentTheme.FontRegular).FontSize(9).FontColor(FlitDocumentTheme.DarkNavy));
    }
}
