using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Flit.Infrastructure.Documents.Branding;

/// <summary>
/// Membrete de marca FLIT para documentos generados con QuestPDF (HU #10855): bandas superior e
/// inferior con la identidad visual, pensadas para colocarse en <c>page.Header()</c> / <c>page.Footer()</c>.
/// El membrete ocupa arriba y abajo el alto del margen (2,54 cm). El <b>nombre del documento</b> no se
/// pinta aquí sino con <see cref="FlitPdfStamper.ApplyDocumentName"/> (va dentro del margen inferior,
/// fuera del área de contenido de QuestPDF), para tener una sola ruta de código reutilizable.
/// </summary>
public static class FlitLetterhead
{
    /// <summary>Banda superior del membrete (vectorial SVG, decisión D4).</summary>
    public static void Header(IContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);
        container.Height(FlitDocumentTheme.MarginCm, Unit.Centimetre)
            .AlignTop()
            .Svg(BrandingAssets.MembreteHeaderSvg);
    }

    /// <summary>Banda inferior del membrete (vectorial SVG, decisión D4).</summary>
    public static void Footer(IContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);
        container.Height(FlitDocumentTheme.MarginCm, Unit.Centimetre)
            .AlignBottom()
            .Svg(BrandingAssets.MembreteFooterSvg);
    }

    /// <summary>
    /// Configura una página QuestPDF con la identidad FLIT (HU #10856): Carta, sin margen, tipografía
    /// Poppins y membrete arriba/abajo. El contenido se coloca con <see cref="Content"/>.
    /// </summary>
    public static void ApplyTo(PageDescriptor page)
    {
        ArgumentNullException.ThrowIfNull(page);
        page.Size(FlitDocumentTheme.Page);
        page.Margin(0);
        page.DefaultTextStyle(t => t.FontFamily(FlitDocumentTheme.FontRegular).FontSize(10));
        page.Header().Element(Header);
        page.Footer().Element(Footer);
    }

    /// <summary>Área de contenido entre las bandas del membrete, con el margen FLIT (2,54 cm horizontal).</summary>
    public static IContainer Content(PageDescriptor page) =>
        Content(page, FlitDocumentTheme.MarginCm, 0.4f);

    /// <summary>
    /// Igual que <see cref="Content(PageDescriptor)"/> pero con el margen del contenido configurable en
    /// ambos ejes (cm). Lo usa el certificado SOAT/RTM (HU #10856) para acercar las tablas al borde y
    /// dejar una separación apenas perceptible respecto a las bandas del membrete.
    /// </summary>
    public static IContainer Content(PageDescriptor page, float horizontalCm, float verticalCm)
    {
        ArgumentNullException.ThrowIfNull(page);
        return page.Content()
            .PaddingHorizontal(horizontalCm, Unit.Centimetre)
            .PaddingVertical(verticalCm, Unit.Centimetre);
    }
}
